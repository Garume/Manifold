using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Manifold.Cli;

public sealed class CliApplication
{
    private static readonly byte[] NewLineUtf8 = [(byte)'\n'];
    private static readonly Task<int> SuccessExitCodeTask = Task.FromResult(CliExitCodes.Success);
    private static readonly Task<int> UsageErrorExitCodeTask = Task.FromResult(CliExitCodes.UsageError);
    private static readonly Task<int> UnavailableExitCodeTask = Task.FromResult(CliExitCodes.Unavailable);
    private readonly IReadOnlyList<OperationDescriptor> operations;
    private readonly ICliInvoker cliInvoker;
    private readonly IFastSyncCliInvoker? fastSyncCliInvoker;
    private readonly IFastCliInvoker? fastCliInvoker;
    private readonly IServiceProvider? services;
    private readonly Stream? rawOutput;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly CliOperationState[] visibleCliOperations;
    private readonly FrozenDictionary<string, CliCommandCandidate[]> commandCandidatesByFirstToken;

    public CliApplication(
        IReadOnlyList<OperationDescriptor> operations,
        ICliInvoker cliInvoker,
        IServiceProvider? services = null,
        Stream? rawOutput = null,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(cliInvoker);

        this.operations = operations;
        this.cliInvoker = cliInvoker;
        fastSyncCliInvoker = cliInvoker as IFastSyncCliInvoker;
        fastCliInvoker = cliInvoker as IFastCliInvoker;
        this.services = services;
        this.rawOutput = rawOutput;
        this.jsonSerializerOptions = jsonSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        (visibleCliOperations, commandCandidatesByFirstToken) = BuildCliState(operations);
    }

    public string GetUsage(string executableName = "app")
    {
        StringBuilder builder = new();
        builder.AppendLine("Usage:");
        foreach (CliOperationState operation in visibleCliOperations)
            builder.AppendLine("  " + GetOperationUsage(operation, executableName));

        builder.AppendLine();
        builder.AppendLine("Options:");
        builder.AppendLine("  --help                Show help for the application or a specific command.");
        builder.AppendLine("  --json                Emit machine-readable JSON instead of text.");
        return builder.ToString().TrimEnd();
    }

    public Task<int> ExecuteAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (TryExecuteArrayFastPath(args, output, error, cancellationToken, out Task<int>? fastPathTask))
            return fastPathTask!;

        return ExecuteSlowPathAsync(args, output, error, cancellationToken);
    }

    public Task<int> ExecuteAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args is string[] commandTokens)
            return ExecuteAsync(commandTokens, output, error, cancellationToken);

        return ExecuteSlowPathAsync(args, output, error, cancellationToken);
    }

    private Task<int> ExecuteSlowPathAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ParsedArguments parsedArguments = ParseArguments(args);
        if (parsedArguments.RequestHelp && parsedArguments.CommandTokens.Count == 0)
            return WriteLineAndReturnAsync(output, GetUsage(), SuccessExitCodeTask);

        if (!TryResolveOperation(parsedArguments.CommandTokens, out CliOperationState? operation, out int consumedTokens))
        {
            if (parsedArguments.CommandTokens.Count == 0)
                return WriteLineAndReturnAsync(output, GetUsage(), SuccessExitCodeTask);

            return WriteLineAndReturnAsync(
                error,
                $"Unknown command '{string.Join(' ', parsedArguments.CommandTokens)}'.",
                UsageErrorExitCodeTask);
        }

        if (parsedArguments.RequestHelp)
            return WriteLineAndReturnAsync(output, GetOperationUsage(operation!, "app"), SuccessExitCodeTask);

        CliOperationState resolvedOperation = operation!;

        try
        {
            ParsedCommandInput input = ParseCommandInput(resolvedOperation, parsedArguments.CommandTokens, consumedTokens);
            if (!cliInvoker.TryInvoke(
                    resolvedOperation.Operation.OperationId,
                    input.Options,
                    input.Arguments,
                    services,
                    parsedArguments.Json,
                    cancellationToken,
                    out ValueTask<CliInvocationResult> invocation))
            {
                throw new InvalidOperationException(
                    $"No generated CLI invoker was available for operation '{resolvedOperation.Operation.OperationId}'.");
            }

            if (invocation.IsCompletedSuccessfully)
                return WriteResultAsync(output, invocation.Result, parsedArguments.Json, cancellationToken);

            return AwaitInvocationAsync(invocation, output, error, parsedArguments.Json, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return WriteLineAndReturnAsync(error, exception.Message, UsageErrorExitCodeTask);
        }
        catch (InvalidOperationException exception)
        {
            return WriteLineAndReturnAsync(error, exception.Message, UnavailableExitCodeTask);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryExecuteArrayFastPath(
        string[] commandTokens,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        out Task<int>? execution)
    {
        execution = null;
        if (commandTokens.Length == 0)
        {
            return false;
        }

        try
        {
            if (fastSyncCliInvoker is not null &&
                fastSyncCliInvoker.TryInvokeFastSync(commandTokens, services, cancellationToken, out FastCliInvocationResult syncInvocation))
            {
                execution = WriteFastResult(output, syncInvocation);
                return true;
            }

            if (fastCliInvoker is null)
                return false;

            if (!fastCliInvoker.TryInvokeFast(commandTokens, services, cancellationToken, out ValueTask<FastCliInvocationResult> invocation))
                return false;

            execution = invocation.IsCompletedSuccessfully
                ? WriteFastResult(output, invocation.Result)
                : AwaitFastInvocationAsync(invocation, output, error);
            return true;
        }
        catch (ArgumentException exception)
        {
            execution = WriteLineAndReturnAsync(error, exception.Message, UsageErrorExitCodeTask);
            return true;
        }
        catch (InvalidOperationException exception)
        {
            execution = WriteLineAndReturnAsync(error, exception.Message, UnavailableExitCodeTask);
            return true;
        }
    }

    private static (CliOperationState[] VisibleCliOperations, FrozenDictionary<string, CliCommandCandidate[]> CommandCandidatesByFirstToken) BuildCliState(
        IReadOnlyList<OperationDescriptor> operations)
    {
        List<CliOperationState> visible = [];
        Dictionary<string, List<CliCommandCandidate>> commandCandidatesByFirstToken = new(StringComparer.OrdinalIgnoreCase);

        foreach (OperationDescriptor operation in operations)
        {
            if (operation.Visibility is OperationVisibility.McpOnly ||
                operation.CliCommandPath is not { Count: > 0 })
            {
                continue;
            }

            CliOperationState state = new(operation);
            if (!operation.Hidden)
                visible.Add(state);

            foreach (string[] commandPath in state.CommandPaths)
            {
                string firstToken = commandPath[0];
                if (!commandCandidatesByFirstToken.TryGetValue(firstToken, out List<CliCommandCandidate>? candidates))
                {
                    candidates = [];
                    commandCandidatesByFirstToken[firstToken] = candidates;
                }

                candidates.Add(new CliCommandCandidate(state, commandPath));
            }
        }

        visible.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.DisplayCommand, right.DisplayCommand));

        Dictionary<string, CliCommandCandidate[]> materializedCandidates = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string firstToken, List<CliCommandCandidate> candidates) in commandCandidatesByFirstToken)
        {
            candidates.Sort(static (left, right) => right.Path.Length.CompareTo(left.Path.Length));
            materializedCandidates[firstToken] = [.. candidates];
        }

        return ([.. visible], materializedCandidates.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private static ParsedArguments ParseArguments(IReadOnlyList<string> args)
    {
        int flagCount = 0;
        bool json = false;
        bool requestHelp = false;

        foreach (string arg in args)
        {
            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                flagCount++;
                continue;
            }

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                requestHelp = true;
                flagCount++;
                continue;
            }
        }

        if (flagCount == 0)
            return new ParsedArguments(args, false, false);

        List<string> commandTokens = new(args.Count - flagCount);
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            commandTokens.Add(arg);
        }

        return new ParsedArguments(commandTokens, json, requestHelp);
    }

    private bool TryResolveOperation(
        IReadOnlyList<string> commandTokens,
        out CliOperationState? operation,
        out int consumedTokens)
    {
        if (commandTokens.Count == 0 ||
            !commandCandidatesByFirstToken.TryGetValue(commandTokens[0], out CliCommandCandidate[]? candidates))
        {
            operation = null;
            consumedTokens = 0;
            return false;
        }

        foreach (CliCommandCandidate candidate in candidates)
        {
            string[] path = candidate.Path;
            if (commandTokens.Count < path.Length)
                continue;

            bool matched = true;
            for (int index = 0; index < path.Length; index++)
            {
                if (string.Equals(commandTokens[index], path[index], StringComparison.OrdinalIgnoreCase))
                    continue;

                matched = false;
                break;
            }

            if (!matched)
                continue;

            operation = candidate.Operation;
            consumedTokens = path.Length;
            return true;
        }

        operation = null;
        consumedTokens = 0;
        return false;
    }

    private static ParsedCommandInput ParseCommandInput(
        CliOperationState operation,
        IReadOnlyList<string> remainingTokens,
        int startIndex)
    {
        Dictionary<string, string> options = new(operation.KnownOptionNames.Count, StringComparer.OrdinalIgnoreCase);
        List<string> arguments = [];
        for (int index = startIndex; index < remainingTokens.Count; index++)
        {
            string current = remainingTokens[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                arguments.Add(current);
                continue;
            }

            string optionToken = current[2..];
            string optionName;
            string optionValue;
            int equalsIndex = optionToken.IndexOf('=');
            if (equalsIndex >= 0)
            {
                optionName = optionToken[..equalsIndex];
                optionValue = optionToken[(equalsIndex + 1)..];
            }
            else
            {
                optionName = optionToken;
                if (++index >= remainingTokens.Count ||
                    string.IsNullOrWhiteSpace(remainingTokens[index]) ||
                    remainingTokens[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"The --{optionName} option requires a non-empty value.");
                }

                optionValue = remainingTokens[index];
            }

            if (string.IsNullOrWhiteSpace(optionName))
                throw new ArgumentException("Encountered an empty option name.");

            options[optionName] = optionValue;
        }

        foreach ((string optionName, _) in options)
        {
            if (!operation.KnownOptionNames.Contains(optionName))
                throw new ArgumentException($"Unknown option '--{optionName}' for command '{operation.DisplayCommand}'.");
        }

        foreach (ParameterDescriptor parameter in operation.RequiredOptionParameters)
        {
            if (CliBinding.TryFindOptionValue(options, GetCliParameterName(parameter), parameter.Aliases, out _))
                continue;

            throw new ArgumentException($"Missing required --{GetCliParameterName(parameter)} option.");
        }

        foreach (ParameterDescriptor parameter in operation.RequiredArgumentParameters)
        {
            if (parameter.Position is not null && parameter.Position.Value < arguments.Count)
                continue;

            throw new ArgumentException($"Missing required argument '{GetCliParameterName(parameter)}'.");
        }

        return new ParsedCommandInput(options, arguments);
    }

    private Task<int> WriteResultAsync(
        TextWriter output,
        CliInvocationResult invocationResult,
        bool json,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (json)
        {
            if (invocationResult.RawJsonPayload is { Length: > 0 } rawJsonPayload)
            {
                if (rawOutput is not null)
                    return WriteRawJsonAsync(rawOutput, rawJsonPayload, cancellationToken);

                return WriteLineAndReturnAsync(output, Encoding.UTF8.GetString(rawJsonPayload), SuccessExitCodeTask);
            }

            string jsonText = JsonSerializer.Serialize(invocationResult.Result, invocationResult.ResultType, jsonSerializerOptions);
            return WriteLineAndReturnAsync(output, jsonText, SuccessExitCodeTask);
        }

        if (string.IsNullOrWhiteSpace(invocationResult.Text))
            return SuccessExitCodeTask;

        return WriteLineAndReturnAsync(output, invocationResult.Text, SuccessExitCodeTask);
    }

    private static Task<int> WriteFastResult(TextWriter output, FastCliInvocationResult result)
    {
        return result.Kind switch
        {
            FastCliInvocationKind.None => SuccessExitCodeTask,
            FastCliInvocationKind.Text => WriteLineAndReturnAsync(output, result.Text!, SuccessExitCodeTask),
            FastCliInvocationKind.Boolean => WriteSpanLineAndReturn(output, result.Boolean ? bool.TrueString : bool.FalseString),
            FastCliInvocationKind.Number => WriteFormattableLineAndReturn(output, result.Number),
            FastCliInvocationKind.LargeNumber => WriteFormattableLineAndReturn(output, result.LargeNumber),
            FastCliInvocationKind.RealNumber => WriteFormattableLineAndReturn(output, result.RealNumber),
            FastCliInvocationKind.PreciseNumber => WriteFormattableLineAndReturn(output, result.PreciseNumber),
            FastCliInvocationKind.Identifier => WriteFormattableLineAndReturn(output, result.Identifier),
            FastCliInvocationKind.Timestamp => WriteFormattableLineAndReturn(output, result.Timestamp),
            _ => throw new InvalidOperationException($"Unsupported fast CLI result kind '{result.Kind}'.")
        };
    }

    private static async Task<int> AwaitFastInvocationAsync(
        ValueTask<FastCliInvocationResult> invocation,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            FastCliInvocationResult result = await invocation.ConfigureAwait(false);
            return await WriteFastResult(output, result).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await WriteLineAndReturnAsync(error, exception.Message, UsageErrorExitCodeTask).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await WriteLineAndReturnAsync(error, exception.Message, UnavailableExitCodeTask).ConfigureAwait(false);
        }
    }

    private async Task<int> AwaitInvocationAsync(
        ValueTask<CliInvocationResult> invocation,
        TextWriter output,
        TextWriter error,
        bool json,
        CancellationToken cancellationToken)
    {
        try
        {
            CliInvocationResult result = await invocation.ConfigureAwait(false);
            return await WriteResultAsync(output, result, json, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return await WriteLineAndReturnAsync(error, exception.Message, UsageErrorExitCodeTask).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await WriteLineAndReturnAsync(error, exception.Message, UnavailableExitCodeTask).ConfigureAwait(false);
        }
    }

    private static async Task<int> WriteRawJsonAsync(Stream output, byte[] payload, CancellationToken cancellationToken)
    {
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(NewLineUtf8, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static Task<int> WriteLineAndReturnAsync(TextWriter writer, string text, Task<int> completion)
    {
        Task writeTask = writer.WriteLineAsync(text);
        return writeTask.IsCompletedSuccessfully
            ? completion
            : AwaitWriteAndReturnAsync(writeTask, completion);
    }

    private static Task<int> WriteSpanLineAndReturn(TextWriter writer, ReadOnlySpan<char> text)
    {
        writer.Write(text);
        writer.WriteLine();
        return SuccessExitCodeTask;
    }

    private static Task<int> WriteFormattableLineAndReturn<T>(TextWriter writer, T value)
        where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out int charsWritten, default, CultureInfo.InvariantCulture))
            return WriteSpanLineAndReturn(writer, buffer[..charsWritten]);

        return WriteLineAndReturnAsync(writer, value.ToString(null, CultureInfo.InvariantCulture), SuccessExitCodeTask);
    }

    private static async Task<int> AwaitWriteAndReturnAsync(Task writeTask, Task<int> completion)
    {
        await writeTask.ConfigureAwait(false);
        return await completion.ConfigureAwait(false);
    }

    private static string GetOperationUsage(CliOperationState operation, string executableName)
    {
        StringBuilder builder = new();
        builder.Append("Usage: ")
            .Append(executableName)
            .Append(' ')
            .Append(operation.DisplayCommand);

        foreach (ParameterDescriptor parameter in operation.ArgumentParameters)
        {
            builder.Append(' ')
                .Append(parameter.Required ? '<' : '[')
                .Append(GetCliParameterName(parameter))
                .Append(parameter.Required ? '>' : ']');
        }

        foreach (ParameterDescriptor parameter in operation.OptionParameters)
        {
            builder.Append(parameter.Required ? " --" : " [--")
                .Append(GetCliParameterName(parameter))
                .Append(" <value>");
            if (!parameter.Required)
                builder.Append(']');
        }

        if (!string.IsNullOrWhiteSpace(operation.Operation.Description))
        {
            builder.AppendLine()
                .Append("  ")
                .Append(operation.Operation.Description);
        }

        if (operation.CommandAliasDisplays.Length > 0)
        {
            builder.AppendLine()
                .Append("  Aliases: ")
                .Append(string.Join(", ", operation.CommandAliasDisplays));
        }

        foreach (ParameterDescriptor parameter in operation.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Description))
                continue;

            builder.AppendLine();
            if (parameter.Source == ParameterSource.Argument)
                builder.Append("  ").Append(GetCliParameterName(parameter));
            else if (parameter.Source == ParameterSource.Option)
                builder.Append("  --").Append(GetCliParameterName(parameter));
            else
                builder.Append("  ").Append(parameter.Name);

            builder.Append(": ").Append(parameter.Description);
        }

        return builder.ToString();
    }

    private readonly record struct ParsedArguments(IReadOnlyList<string> CommandTokens, bool Json, bool RequestHelp);

    private readonly record struct ParsedCommandInput(
        IReadOnlyDictionary<string, string> Options,
        IReadOnlyList<string> Arguments);

    private sealed class CliOperationState
    {
        public CliOperationState(OperationDescriptor operation)
        {
            Operation = operation;
            IReadOnlyList<string> primaryCommandPath = operation.CliCommandPath
                ?? throw new InvalidOperationException($"CLI operation '{operation.OperationId}' was missing a command path.");

            DisplayCommand = string.Join(' ', primaryCommandPath);

            List<string[]> commandPaths = [MaterializeCommandPath(primaryCommandPath)];
            if (operation.CliCommandAliases is { Count: > 0 })
            {
                foreach (IReadOnlyList<string> aliasPath in operation.CliCommandAliases)
                    commandPaths.Add(MaterializeCommandPath(aliasPath));
            }

            CommandPaths = [.. commandPaths];
            CommandAliasDisplays = operation.CliCommandAliases is { Count: > 0 }
                ? [.. operation.CliCommandAliases.Select(static aliasPath => string.Join(' ', aliasPath))]
                : [];

            List<ParameterDescriptor> argumentParameters = [];
            List<ParameterDescriptor> optionParameters = [];
            List<ParameterDescriptor> requiredArgumentParameters = [];
            List<ParameterDescriptor> requiredOptionParameters = [];
            HashSet<string> knownOptionNames = new(StringComparer.OrdinalIgnoreCase);

            Parameters = operation.Parameters is ParameterDescriptor[] parameterArray
                ? parameterArray
                : [.. operation.Parameters];

            foreach (ParameterDescriptor parameter in Parameters)
            {
                switch (parameter.Source)
                {
                    case ParameterSource.Argument:
                        argumentParameters.Add(parameter);
                        if (parameter.Required)
                            requiredArgumentParameters.Add(parameter);
                        break;
                    case ParameterSource.Option:
                        optionParameters.Add(parameter);
                        knownOptionNames.Add(GetCliParameterName(parameter));
                        if (parameter.Aliases is not null)
                        {
                            foreach (string alias in parameter.Aliases)
                                knownOptionNames.Add(alias);
                        }

                        if (parameter.Required)
                            requiredOptionParameters.Add(parameter);
                        break;
                }
            }

            argumentParameters.Sort(static (left, right) => Nullable.Compare(left.Position, right.Position));
            ArgumentParameters = [.. argumentParameters];
            OptionParameters = [.. optionParameters];
            RequiredArgumentParameters = [.. requiredArgumentParameters];
            RequiredOptionParameters = [.. requiredOptionParameters];
            KnownOptionNames = knownOptionNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }

        public OperationDescriptor Operation { get; }
        public string DisplayCommand { get; }
        public string[] CommandAliasDisplays { get; }
        public string[][] CommandPaths { get; }
        public ParameterDescriptor[] Parameters { get; }
        public ParameterDescriptor[] ArgumentParameters { get; }
        public ParameterDescriptor[] OptionParameters { get; }
        public ParameterDescriptor[] RequiredArgumentParameters { get; }
        public ParameterDescriptor[] RequiredOptionParameters { get; }
        public FrozenSet<string> KnownOptionNames { get; }

        private static string[] MaterializeCommandPath(IReadOnlyList<string> path)
        {
            string[] materialized = new string[path.Count];
            for (int index = 0; index < path.Count; index++)
                materialized[index] = path[index];

            return materialized;
        }
    }

    private readonly record struct CliCommandCandidate(CliOperationState Operation, string[] Path);

    private static string GetCliParameterName(ParameterDescriptor parameter)
    {
        return string.IsNullOrWhiteSpace(parameter.CliName) ? parameter.Name : parameter.CliName;
    }
}


