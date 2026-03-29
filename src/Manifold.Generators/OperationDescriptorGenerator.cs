using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Manifold.Generators;

[Generator]
public sealed class OperationDescriptorGenerator : IIncrementalGenerator
{
    private const string OperationAttributeMetadataName = "Manifold.OperationAttribute";
    private const string OptionAttributeMetadataName = "Manifold.OptionAttribute";
    private const string ArgumentAttributeMetadataName = "Manifold.ArgumentAttribute";
    private const string AliasAttributeMetadataName = "Manifold.AliasAttribute";
    private const string CliCommandAttributeMetadataName = "Manifold.CliCommandAttribute";
    private const string CliNameAttributeMetadataName = "Manifold.CliNameAttribute";
    private const string McpToolAttributeMetadataName = "Manifold.McpToolAttribute";
    private const string McpNameAttributeMetadataName = "Manifold.McpNameAttribute";
    private const string CliOnlyAttributeMetadataName = "Manifold.CliOnlyAttribute";
    private const string McpOnlyAttributeMetadataName = "Manifold.McpOnlyAttribute";
    private const string FromServicesAttributeMetadataName = "Manifold.FromServicesAttribute";
    private const string ResultFormatterAttributeMetadataName = "Manifold.ResultFormatterAttribute";
    private const string OperationInterfaceMetadataName = "Manifold.IOperation<TRequest, TResult>";
    private const string OperationContextMetadataName = "Manifold.OperationContext";
    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;
    private static readonly DiagnosticDescriptor ConflictingVisibilityDescriptor = new(
        id: "DMCF001",
        title: "Conflicting CLI and MCP visibility",
        messageFormat: "Operation '{0}' cannot be marked with both [CliOnly] and [McpOnly]",
        category: "Manifold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor ConflictingParameterBindingDescriptor = new(
        id: "DMCF002",
        title: "Conflicting parameter binding",
        messageFormat: "Parameter '{0}' on operation '{1}' cannot be marked with both [Option] and [Argument]",
        category: "Manifold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor UnsupportedParameterBindingDescriptor = new(
        id: "DMCF003",
        title: "Unsupported parameter binding",
        messageFormat: "Parameter '{0}' on operation '{1}' must be bound with [Option], [Argument], [FromServices], or be a CancellationToken",
        category: "Manifold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor UnsupportedOperationClassDescriptor = new(
        id: "DMCF004",
        title: "Unsupported operation class",
        messageFormat: "Operation class '{0}' must implement IOperation<TRequest, TResult> and expose ExecuteAsync(TRequest, OperationContext)",
        category: "Manifold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor UnsupportedRequestPropertyBindingDescriptor = new(
        id: "DMCF005",
        title: "Unsupported request property binding",
        messageFormat: "Property '{0}' on request type '{1}' for operation '{2}' must be writable with a public init or set accessor",
        category: "Manifold",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<OperationAnalysisResult> candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OperationAttributeMetadataName,
                static (_, _) => true,
                static (attributeContext, cancellationToken) => CreateCandidate(attributeContext, cancellationToken));

        IncrementalValueProvider<(Compilation Left, ImmutableArray<OperationAnalysisResult> Right)> generationInputs =
            context.CompilationProvider.Combine(candidates.Collect());

        context.RegisterSourceOutput(generationInputs, static (productionContext, input) =>
            Execute(productionContext, input.Left, input.Right));
    }

    private static OperationAnalysisResult CreateCandidate(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AttributeData operationAttribute = context.Attributes[0];
        return context.TargetSymbol switch
        {
            IMethodSymbol methodSymbol => CreateMethodCandidate(context, methodSymbol, operationAttribute, cancellationToken),
            INamedTypeSymbol typeSymbol => CreateClassCandidate(context, typeSymbol, operationAttribute, cancellationToken),
            _ => new OperationAnalysisResult(null, [])
        };
    }

    private static OperationAnalysisResult CreateMethodCandidate(
        GeneratorAttributeSyntaxContext context,
        IMethodSymbol methodSymbol,
        AttributeData operationAttribute,
        CancellationToken cancellationToken)
    {
        ImmutableArray<OperationDiagnostic>.Builder diagnosticBuilder = ImmutableArray.CreateBuilder<OperationDiagnostic>();

        bool hasCliOnly = HasAttribute(methodSymbol, CliOnlyAttributeMetadataName);
        bool hasMcpOnly = HasAttribute(methodSymbol, McpOnlyAttributeMetadataName);
        if (hasCliOnly && hasMcpOnly)
        {
            diagnosticBuilder.Add(new OperationDiagnostic(
                ConflictingVisibilityDescriptor,
                GetBestLocation(methodSymbol),
                [methodSymbol.Name]));
        }

        ImmutableArray<ParameterCandidate>.Builder parameterBuilder = ImmutableArray.CreateBuilder<ParameterCandidate>();
        foreach (IParameterSymbol parameterSymbol in methodSymbol.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ParameterAnalysisResult parameterAnalysis = CreateParameterCandidate(methodSymbol.Name, parameterSymbol);
            diagnosticBuilder.AddRange(parameterAnalysis.Diagnostics);
            if (parameterAnalysis.Candidate is not null)
                parameterBuilder.Add(parameterAnalysis.Candidate);
        }

        if (diagnosticBuilder.Count > 0)
            return new OperationAnalysisResult(null, diagnosticBuilder.ToImmutable());

        ImmutableArray<string>? cliCommandPath = GetCliCommandPath(methodSymbol);
        string? cliName = GetSingleName(methodSymbol, CliNameAttributeMetadataName);
        if ((cliCommandPath is null || cliCommandPath.Value.IsDefaultOrEmpty) && !string.IsNullOrWhiteSpace(cliName))
            cliCommandPath = [cliName!];

        ImmutableArray<ImmutableArray<string>>? cliCommandAliases = GetCliCommandAliases(methodSymbol, cliCommandPath);
        MethodReturnKind methodReturnKind = GetMethodReturnKind(methodSymbol.ReturnType);
        ITypeSymbol resultType = UnwrapResultType(methodSymbol.ReturnType, context.SemanticModel.Compilation);
        string? mcpToolName = GetSingleName(methodSymbol, McpToolAttributeMetadataName) ??
                              GetSingleName(methodSymbol, McpNameAttributeMetadataName) ??
                              (hasCliOnly ? null : (string)operationAttribute.ConstructorArguments[0].Value!);

        return new OperationAnalysisResult(
            new OperationCandidate(
                (string)operationAttribute.ConstructorArguments[0].Value!,
                methodSymbol.ContainingType.ToDisplayString(FullyQualifiedTypeFormat),
                methodSymbol.Name,
                methodSymbol.ReturnType.ToDisplayString(FullyQualifiedTypeFormat),
                resultType.ToDisplayString(FullyQualifiedTypeFormat),
                methodReturnKind,
                GetVisibility(hasCliOnly, hasMcpOnly),
                parameterBuilder.ToImmutable(),
                GetNamedString(operationAttribute, "Description"),
                GetNamedString(operationAttribute, "Summary"),
                cliCommandPath,
                cliCommandAliases,
                mcpToolName,
                GetFormatterTypeName(methodSymbol),
                GetNamedBoolean(operationAttribute, "Hidden"),
                InvocationKind.StaticMethod,
                null),
            diagnosticBuilder.ToImmutable());
    }

    private static OperationAnalysisResult CreateClassCandidate(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol typeSymbol,
        AttributeData operationAttribute,
        CancellationToken cancellationToken)
    {
        ImmutableArray<OperationDiagnostic>.Builder diagnosticBuilder = ImmutableArray.CreateBuilder<OperationDiagnostic>();

        bool hasCliOnly = HasAttribute(typeSymbol, CliOnlyAttributeMetadataName);
        bool hasMcpOnly = HasAttribute(typeSymbol, McpOnlyAttributeMetadataName);
        if (hasCliOnly && hasMcpOnly)
        {
            diagnosticBuilder.Add(new OperationDiagnostic(
                ConflictingVisibilityDescriptor,
                GetBestLocation(typeSymbol),
                [typeSymbol.Name]));
        }

        INamedTypeSymbol? operationInterface = typeSymbol.AllInterfaces.FirstOrDefault(static candidate =>
            string.Equals(candidate.OriginalDefinition.ToDisplayString(), OperationInterfaceMetadataName, StringComparison.Ordinal));
        if (operationInterface is null)
        {
            diagnosticBuilder.Add(new OperationDiagnostic(
                UnsupportedOperationClassDescriptor,
                GetBestLocation(typeSymbol),
                [typeSymbol.Name]));
            return new OperationAnalysisResult(null, diagnosticBuilder.ToImmutable());
        }

        ITypeSymbol requestType = operationInterface.TypeArguments[0];
        IMethodSymbol? executeMethod = FindExecuteMethod(typeSymbol, requestType);
        if (executeMethod is null)
        {
            diagnosticBuilder.Add(new OperationDiagnostic(
                UnsupportedOperationClassDescriptor,
                GetBestLocation(typeSymbol),
                [typeSymbol.Name]));
            return new OperationAnalysisResult(null, diagnosticBuilder.ToImmutable());
        }

        if (requestType is INamedTypeSymbol requestTypeSymbol)
        {
            ImmutableArray<ParameterCandidate>.Builder parameterBuilder = ImmutableArray.CreateBuilder<ParameterCandidate>();
            foreach (IPropertySymbol propertySymbol in GetBindableProperties(requestTypeSymbol))
            {
                cancellationToken.ThrowIfCancellationRequested();

                ParameterAnalysisResult parameterAnalysis = CreatePropertyCandidate(
                    typeSymbol.Name,
                    requestTypeSymbol,
                    propertySymbol);
                diagnosticBuilder.AddRange(parameterAnalysis.Diagnostics);
                if (parameterAnalysis.Candidate is not null)
                    parameterBuilder.Add(parameterAnalysis.Candidate);
            }

            if (diagnosticBuilder.Count > 0)
                return new OperationAnalysisResult(null, diagnosticBuilder.ToImmutable());

            ImmutableArray<string>? cliCommandPath = GetCliCommandPath(typeSymbol);
            string? cliName = GetSingleName(typeSymbol, CliNameAttributeMetadataName);
            if ((cliCommandPath is null || cliCommandPath.Value.IsDefaultOrEmpty) && !string.IsNullOrWhiteSpace(cliName))
                cliCommandPath = [cliName!];

            ImmutableArray<ImmutableArray<string>>? cliCommandAliases = GetCliCommandAliases(typeSymbol, cliCommandPath);
            string? mcpToolName = GetSingleName(typeSymbol, McpToolAttributeMetadataName) ??
                                  GetSingleName(typeSymbol, McpNameAttributeMetadataName) ??
                                  (hasCliOnly ? null : (string)operationAttribute.ConstructorArguments[0].Value!);

            return new OperationAnalysisResult(
                new OperationCandidate(
                    (string)operationAttribute.ConstructorArguments[0].Value!,
                    typeSymbol.ToDisplayString(FullyQualifiedTypeFormat),
                    executeMethod.Name,
                    executeMethod.ReturnType.ToDisplayString(FullyQualifiedTypeFormat),
                    operationInterface.TypeArguments[1].ToDisplayString(FullyQualifiedTypeFormat),
                    GetMethodReturnKind(executeMethod.ReturnType),
                    GetVisibility(hasCliOnly, hasMcpOnly),
                    parameterBuilder.ToImmutable(),
                    GetNamedString(operationAttribute, "Description"),
                    GetNamedString(operationAttribute, "Summary"),
                    cliCommandPath,
                    cliCommandAliases,
                    mcpToolName,
                    GetFormatterTypeName(typeSymbol),
                    GetNamedBoolean(operationAttribute, "Hidden"),
                    InvocationKind.InstanceOperation,
                    requestTypeSymbol.ToDisplayString(FullyQualifiedTypeFormat)),
                diagnosticBuilder.ToImmutable());
        }

        diagnosticBuilder.Add(new OperationDiagnostic(
            UnsupportedOperationClassDescriptor,
            GetBestLocation(typeSymbol),
            [typeSymbol.Name]));
        return new OperationAnalysisResult(null, diagnosticBuilder.ToImmutable());
    }

    private static IMethodSymbol? FindExecuteMethod(INamedTypeSymbol typeSymbol, ITypeSymbol requestType)
    {
        return typeSymbol.GetMembers("ExecuteAsync")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(methodSymbol =>
                !methodSymbol.IsStatic &&
                methodSymbol.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(methodSymbol.Parameters[0].Type, requestType) &&
                string.Equals(methodSymbol.Parameters[1].Type.ToDisplayString(), OperationContextMetadataName, StringComparison.Ordinal));
    }

    private static IEnumerable<IPropertySymbol> GetBindableProperties(INamedTypeSymbol requestType)
    {
        return requestType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static property =>
                !property.IsStatic &&
                property.Parameters.Length == 0 &&
                (HasAttribute(property, OptionAttributeMetadataName) || HasAttribute(property, ArgumentAttributeMetadataName)));
    }

    private static string? GetFormatterTypeName(ISymbol symbol)
    {
        AttributeData? formatterAttribute = GetAttribute(symbol, ResultFormatterAttributeMetadataName);
        if (formatterAttribute is null || formatterAttribute.ConstructorArguments.Length == 0)
            return null;

        return formatterAttribute.ConstructorArguments[0].Value is ITypeSymbol formatterType
            ? formatterType.ToDisplayString(FullyQualifiedTypeFormat)
            : null;
    }

    private static ParameterAnalysisResult CreateParameterCandidate(
        string operationName,
        IParameterSymbol parameterSymbol)
    {
        bool isCancellationToken = IsCancellationToken(parameterSymbol.Type);
        bool hasFromServices = HasAttribute(parameterSymbol, FromServicesAttributeMetadataName);
        AttributeData? optionAttribute = GetAttribute(parameterSymbol, OptionAttributeMetadataName);
        AttributeData? argumentAttribute = GetAttribute(parameterSymbol, ArgumentAttributeMetadataName);

        if (optionAttribute is not null && argumentAttribute is not null)
        {
            return ParameterAnalysisResult.FromDiagnostic(
                new OperationDiagnostic(
                    ConflictingParameterBindingDescriptor,
                    GetBestLocation(parameterSymbol),
                    [parameterSymbol.Name, operationName]));
        }

        if (!isCancellationToken && !hasFromServices && optionAttribute is null && argumentAttribute is null)
        {
            return ParameterAnalysisResult.FromDiagnostic(
                new OperationDiagnostic(
                    UnsupportedParameterBindingDescriptor,
                    GetBestLocation(parameterSymbol),
                    [parameterSymbol.Name, operationName]));
        }

        ImmutableArray<string>? aliases = GetAliases(parameterSymbol);
        string parameterTypeName = parameterSymbol.Type.ToDisplayString(FullyQualifiedTypeFormat);
        string? cliName = GetSingleName(parameterSymbol, CliNameAttributeMetadataName);
        string? mcpName = GetSingleName(parameterSymbol, McpNameAttributeMetadataName);

        if (isCancellationToken)
            return ParameterAnalysisResult.FromCandidate(
                new ParameterCandidate(parameterSymbol.Name, parameterTypeName, ParameterSourceCandidate.CancellationToken, false, null, null, aliases, cliName, mcpName));

        if (hasFromServices)
            return ParameterAnalysisResult.FromCandidate(
                new ParameterCandidate(parameterSymbol.Name, parameterTypeName, ParameterSourceCandidate.Service, false, null, null, aliases, cliName, mcpName));

        if (optionAttribute is not null)
        {
            return ParameterAnalysisResult.FromCandidate(new ParameterCandidate(
                (string)optionAttribute.ConstructorArguments[0].Value!,
                parameterTypeName,
                ParameterSourceCandidate.Option,
                GetNamedBoolean(optionAttribute, "Required", true),
                null,
                GetNamedString(optionAttribute, "Description"),
                aliases,
                cliName,
                mcpName));
        }

        return ParameterAnalysisResult.FromCandidate(new ParameterCandidate(
            GetNamedString(argumentAttribute!, "Name") ?? parameterSymbol.Name,
            parameterTypeName,
            ParameterSourceCandidate.Argument,
            GetNamedBoolean(argumentAttribute!, "Required", true),
            (int)argumentAttribute!.ConstructorArguments[0].Value!,
            GetNamedString(argumentAttribute!, "Description"),
            aliases,
            cliName,
            mcpName));
    }

    private static ParameterAnalysisResult CreatePropertyCandidate(
        string operationName,
        INamedTypeSymbol requestTypeSymbol,
        IPropertySymbol propertySymbol)
    {
        AttributeData? optionAttribute = GetAttribute(propertySymbol, OptionAttributeMetadataName);
        AttributeData? argumentAttribute = GetAttribute(propertySymbol, ArgumentAttributeMetadataName);

        if (optionAttribute is not null && argumentAttribute is not null)
        {
            return ParameterAnalysisResult.FromDiagnostic(
                new OperationDiagnostic(
                    ConflictingParameterBindingDescriptor,
                    GetBestLocation(propertySymbol),
                    [propertySymbol.Name, operationName]));
        }

        if (propertySymbol.SetMethod is null || propertySymbol.SetMethod.DeclaredAccessibility != Accessibility.Public)
        {
            return ParameterAnalysisResult.FromDiagnostic(
                new OperationDiagnostic(
                    UnsupportedRequestPropertyBindingDescriptor,
                    GetBestLocation(propertySymbol),
                    [propertySymbol.Name, requestTypeSymbol.Name, operationName]));
        }

        ImmutableArray<string>? aliases = GetAliases(propertySymbol);
        string parameterTypeName = propertySymbol.Type.ToDisplayString(FullyQualifiedTypeFormat);
        string? cliName = GetSingleName(propertySymbol, CliNameAttributeMetadataName);
        string? mcpName = GetSingleName(propertySymbol, McpNameAttributeMetadataName);

        if (optionAttribute is not null)
        {
            return ParameterAnalysisResult.FromCandidate(new ParameterCandidate(
                (string)optionAttribute.ConstructorArguments[0].Value!,
                parameterTypeName,
                ParameterSourceCandidate.Option,
                GetNamedBoolean(optionAttribute, "Required", true),
                null,
                GetNamedString(optionAttribute, "Description"),
                aliases,
                cliName,
                mcpName,
                propertySymbol.Name));
        }

        return ParameterAnalysisResult.FromCandidate(new ParameterCandidate(
            GetNamedString(argumentAttribute!, "Name") ?? propertySymbol.Name,
            parameterTypeName,
            ParameterSourceCandidate.Argument,
            GetNamedBoolean(argumentAttribute!, "Required", true),
            (int)argumentAttribute!.ConstructorArguments[0].Value!,
            GetNamedString(argumentAttribute!, "Description"),
            aliases,
            cliName,
            mcpName,
            propertySymbol.Name));
    }

    private static OperationVisibilityCandidate GetVisibility(bool hasCliOnly, bool hasMcpOnly)
    {
        return hasCliOnly
            ? OperationVisibilityCandidate.CliOnly
            : hasMcpOnly
                ? OperationVisibilityCandidate.McpOnly
                : OperationVisibilityCandidate.Both;
    }

    private static ImmutableArray<string>? GetCliCommandPath(ISymbol symbol)
    {
        AttributeData? cliCommandAttribute = GetAttribute(symbol, CliCommandAttributeMetadataName);
        if (cliCommandAttribute is null || cliCommandAttribute.ConstructorArguments.Length == 0)
            return null;

        TypedConstant pathArgument = cliCommandAttribute.ConstructorArguments[0];
        if (pathArgument.IsNull || pathArgument.Kind != TypedConstantKind.Array)
            return null;

        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();
        foreach (TypedConstant value in pathArgument.Values)
        {
            if (value.Value is string segment && !string.IsNullOrWhiteSpace(segment))
                builder.Add(segment.Trim());
        }

        return builder.Count == 0 ? null : builder.ToImmutable();
    }

    private static ImmutableArray<string>? GetAliases(ISymbol symbol)
    {
        List<string> aliases = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (AttributeData aliasAttribute in symbol.GetAttributes().Where(static attribute => IsAttribute(attribute, AliasAttributeMetadataName)))
        {
            if (aliasAttribute.ConstructorArguments.Length == 0)
                continue;

            TypedConstant aliasesArgument = aliasAttribute.ConstructorArguments[0];
            if (aliasesArgument.IsNull || aliasesArgument.Kind != TypedConstantKind.Array)
                continue;

            foreach (TypedConstant value in aliasesArgument.Values)
            {
                if (value.Value is string alias && !string.IsNullOrWhiteSpace(alias))
                {
                    string normalizedAlias = alias.Trim();
                    if (seen.Add(normalizedAlias))
                        aliases.Add(normalizedAlias);
                }
            }
        }

        return aliases.Count == 0 ? null : [.. aliases];
    }

    private static ImmutableArray<ImmutableArray<string>>? GetCliCommandAliases(
        ISymbol symbol,
        ImmutableArray<string>? cliCommandPath)
    {
        ImmutableArray<string>? aliases = GetAliases(symbol);
        if (aliases is null || aliases.Value.IsDefaultOrEmpty || cliCommandPath is null || cliCommandPath.Value.IsDefaultOrEmpty)
            return null;

        List<ImmutableArray<string>> aliasPaths = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        ImmutableArray<string> primaryPath = cliCommandPath.Value;
        foreach (string alias in aliases.Value)
        {
            ImmutableArray<string>? aliasPath = BuildCliAliasPath(primaryPath, alias);
            if (aliasPath is null || aliasPath.Value.IsDefaultOrEmpty)
                continue;

            string key = string.Join("\u001F", aliasPath.Value);
            if (!seen.Add(key))
                continue;

            aliasPaths.Add(aliasPath.Value);
        }

        return aliasPaths.Count == 0 ? null : [.. aliasPaths];
    }

    private static ImmutableArray<string>? BuildCliAliasPath(ImmutableArray<string> primaryPath, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        string[] segments = alias
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment => segment.Trim())
            .Where(static segment => segment.Length > 0)
            .ToArray();
        if (segments.Length == 0)
            return null;

        if (segments.Length == 1)
        {
            if (primaryPath.Length == 0)
                return [segments[0]];

            ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(primaryPath.Length);
            for (int index = 0; index < primaryPath.Length - 1; index++)
                builder.Add(primaryPath[index]);

            builder.Add(segments[0]);
            return builder.ToImmutable();
        }

        return [.. segments];
    }

    private static ITypeSymbol UnwrapResultType(ITypeSymbol returnType, Compilation compilation)
    {
        if (returnType is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericTaskLike("System.Threading.Tasks.Task") ||
                namedType.IsGenericTaskLike("System.Threading.Tasks.ValueTask"))
                return namedType.TypeArguments[0];

            if (namedType.IsNonGenericTaskLike("System.Threading.Tasks.Task") ||
                namedType.IsNonGenericTaskLike("System.Threading.Tasks.ValueTask"))
                return compilation.GetSpecialType(SpecialType.System_Void);
        }

        return returnType;
    }

    private static MethodReturnKind GetMethodReturnKind(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericTaskLike("System.Threading.Tasks.Task"))
                return MethodReturnKind.TaskOfT;

            if (namedType.IsGenericTaskLike("System.Threading.Tasks.ValueTask"))
                return MethodReturnKind.ValueTaskOfT;

            if (namedType.IsNonGenericTaskLike("System.Threading.Tasks.Task"))
                return MethodReturnKind.Task;

            if (namedType.IsNonGenericTaskLike("System.Threading.Tasks.ValueTask"))
                return MethodReturnKind.ValueTask;
        }

        return returnType.SpecialType == SpecialType.System_Void
            ? MethodReturnKind.Void
            : MethodReturnKind.Value;
    }

    private static bool IsCancellationToken(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               string.Equals(namedType.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);
    }

    private static string? GetSingleName(ISymbol symbol, string metadataName)
    {
        AttributeData? attribute = GetAttribute(symbol, metadataName);
        return attribute is null || attribute.ConstructorArguments.Length == 0
            ? null
            : attribute.ConstructorArguments[0].Value as string;
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().FirstOrDefault(attribute => IsAttribute(attribute, metadataName));
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute => IsAttribute(attribute, metadataName));
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        return string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal);
    }

    private static Location? GetBestLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? symbol.Locations.FirstOrDefault();
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
        {
            if (string.Equals(namedArgument.Key, name, StringComparison.Ordinal))
                return namedArgument.Value.Value as string;
        }

        return null;
    }

    private static bool GetNamedBoolean(AttributeData attribute, string name, bool defaultValue = false)
    {
        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
        {
            if (string.Equals(namedArgument.Key, name, StringComparison.Ordinal) && namedArgument.Value.Value is bool value)
                return value;
        }

        return defaultValue;
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<OperationAnalysisResult> collectedCandidates)
    {
        List<OperationCandidate> operations = [];
        foreach (OperationAnalysisResult analysisResult in collectedCandidates)
        {
            foreach (OperationDiagnostic diagnostic in analysisResult.Diagnostics)
                context.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location, diagnostic.MessageArgs));

            if (analysisResult.Candidate is not null)
                operations.Add(analysisResult.Candidate);
        }

        operations.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.OperationId, right.OperationId));

        if (operations.Count == 0)
            return;

        context.AddSource(
            "GeneratedOperationRegistry.g.cs",
            SourceText.From(GenerateSource(operations), Encoding.UTF8));

        if (compilation.GetTypeByMetadataName("Manifold.IOperationInvoker") is not null &&
            compilation.GetTypeByMetadataName("Manifold.OperationInvocationResult") is not null &&
            compilation.GetTypeByMetadataName("Manifold.OperationBinding") is not null)
        {
            context.AddSource(
                "GeneratedOperationInvoker.g.cs",
                SourceText.From(GenerateOperationInvokerSource(operations), Encoding.UTF8));
        }

        if (compilation.GetTypeByMetadataName("Manifold.Cli.ICliInvoker") is not null &&
            compilation.GetTypeByMetadataName("Manifold.Cli.CliInvocationResult") is not null)
        {
            context.AddSource(
                "GeneratedCliInvoker.g.cs",
                SourceText.From(GenerateCliInvokerSource(operations), Encoding.UTF8));
        }

        if (compilation.GetTypeByMetadataName("Manifold.Mcp.McpBinding") is not null &&
            compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerToolTypeAttribute") is not null &&
            compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions") is not null)
        {
            context.AddSource(
                "GeneratedMcpTools.g.cs",
                SourceText.From(GenerateMcpToolsSource(operations), Encoding.UTF8));
        }

        if (compilation.GetTypeByMetadataName("Manifold.Mcp.McpToolDescriptor") is not null &&
            compilation.GetTypeByMetadataName("Manifold.Mcp.McpParameterDescriptor") is not null)
        {
            context.AddSource(
                "GeneratedMcpCatalog.g.cs",
                SourceText.From(GenerateMcpCatalogSource(operations), Encoding.UTF8));
        }

        if (compilation.GetTypeByMetadataName("Manifold.Mcp.IMcpToolInvoker") is not null &&
            compilation.GetTypeByMetadataName("Manifold.Mcp.IFastMcpToolInvoker") is not null &&
            compilation.GetTypeByMetadataName("Manifold.Mcp.IFastSyncMcpToolInvoker") is not null &&
            compilation.GetTypeByMetadataName("Manifold.Mcp.FastMcpInvocationResult") is not null &&
            compilation.GetTypeByMetadataName("Manifold.OperationInvocationResult") is not null)
        {
            context.AddSource(
                "GeneratedMcpInvoker.g.cs",
                SourceText.From(GenerateMcpInvokerSource(operations), Encoding.UTF8));
        }
    }

    private static string GenerateSource(IReadOnlyList<OperationCandidate> operations)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Manifold.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class GeneratedOperationRegistry");
        builder.AppendLine("{");
        builder.AppendLine("    private static readonly global::Manifold.OperationDescriptor[] operations =");
        builder.AppendLine("    [");
        foreach (OperationCandidate operation in operations)
            AppendOperation(builder, operation);

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::Manifold.OperationDescriptor> Operations => operations;");
        builder.AppendLine();
        builder.AppendLine("    public static bool TryFind(string operationId, out global::Manifold.OperationDescriptor? descriptor)");
        builder.AppendLine("    {");
        builder.AppendLine("        foreach (global::Manifold.OperationDescriptor operation in operations)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (global::System.String.Equals(operation.OperationId, operationId, global::System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                descriptor = operation;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        descriptor = null;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateOperationInvokerSource(IReadOnlyList<OperationCandidate> operations)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Manifold.Generated;");
        builder.AppendLine();
        builder.AppendLine("public sealed class GeneratedOperationInvoker : global::Manifold.IOperationInvoker");
        builder.AppendLine("{");
        builder.AppendLine("    public bool TryInvoke(");
        builder.AppendLine("        string operationId,");
        builder.AppendLine("        object? request,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::Manifold.InvocationSurface surface,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::System.Threading.Tasks.ValueTask<global::Manifold.OperationInvocationResult> invocation)");
        builder.AppendLine("    {");
        foreach (OperationCandidate operation in operations.Where(static operation => operation.InvocationKind == InvocationKind.InstanceOperation))
        {
            builder.Append("        if (global::System.String.Equals(operationId, ")
                .Append(ToLiteral(operation.OperationId))
                .AppendLine(", global::System.StringComparison.Ordinal))");
            builder.AppendLine("        {");
            builder.Append("            invocation = Invoke")
                .Append(GetOperationMethodBaseName(operation.OperationId))
                .AppendLine("Async(request, services, surface, cancellationToken);");
            builder.AppendLine("            return true;");
            builder.AppendLine("        }");
        }

        builder.AppendLine();
        builder.AppendLine("        invocation = default;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();

        foreach (OperationCandidate operation in operations.Where(static operation => operation.InvocationKind == InvocationKind.InstanceOperation))
            AppendOperationInvoker(builder, operation);

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateCliInvokerSource(IReadOnlyList<OperationCandidate> operations)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Manifold.Generated;");
        builder.AppendLine();
        builder.AppendLine("public sealed class GeneratedCliInvoker : global::Manifold.Cli.ICliInvoker, global::Manifold.Cli.IFastSyncCliInvoker, global::Manifold.Cli.IFastCliInvoker");
        builder.AppendLine("{");
        builder.AppendLine("    public bool TryInvokeFastSync(");
        builder.AppendLine("        string[] commandTokens,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::Manifold.Cli.FastCliInvocationResult invocation)");
        builder.AppendLine("    {");
        foreach ((OperationCandidate operation, ImmutableArray<string> commandPath) in operations
                     .Where(static operation =>
                         operation.Visibility is not OperationVisibilityCandidate.McpOnly &&
                         CanUseFastSyncCliInvoker(operation))
                     .SelectMany(static operation => GetCliCommandPaths(operation).Select(commandPath => (operation, commandPath)))
                     .OrderByDescending(static candidate => candidate.commandPath.Length)
                     .ThenBy(static candidate => candidate.operation.OperationId, StringComparer.Ordinal))
        {
            builder.Append("        if (commandTokens.Length >= ")
                .Append(commandPath.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine(")");
            builder.AppendLine("        {");
            builder.Append("            if (");
            for (int index = 0; index < commandPath.Length; index++)
            {
                if (index > 0)
                    builder.AppendLine().Append("                && ");

                builder.Append('(')
                    .Append("commandTokens[")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("] == ")
                    .Append(ToLiteral(commandPath[index]))
                    .Append(" || global::System.String.Equals(commandTokens[")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("], ")
                    .Append(ToLiteral(commandPath[index]))
                    .Append(", global::System.StringComparison.OrdinalIgnoreCase))");
            }

            builder.AppendLine(")");
            builder.AppendLine("            {");
            builder.Append("                if (global::Manifold.Cli.CliBinding.ContainsReservedGlobalFlag(commandTokens, ")
                .Append(commandPath.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine("))");
            builder.AppendLine("                {");
            builder.AppendLine("                    invocation = default;");
            builder.AppendLine("                    return false;");
            builder.AppendLine("                }");
            builder.AppendLine();
            builder.Append("                invocation = Invoke")
                .Append(GetOperationMethodBaseName(operation.OperationId))
                .Append("FastSync(commandTokens, ")
                .Append(commandPath.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine(", services, cancellationToken);");
            builder.AppendLine("                return true;");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
        }

        builder.AppendLine();
        builder.AppendLine("        invocation = default;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public bool TryInvokeFast(");
        builder.AppendLine("        string[] commandTokens,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::System.Threading.Tasks.ValueTask<global::Manifold.Cli.FastCliInvocationResult> invocation)");
        builder.AppendLine("    {");
        foreach ((OperationCandidate operation, ImmutableArray<string> commandPath) in operations
                     .Where(static operation =>
                         operation.Visibility is not OperationVisibilityCandidate.McpOnly &&
                         CanUseFastCliInvoker(operation))
                     .SelectMany(static operation => GetCliCommandPaths(operation).Select(commandPath => (operation, commandPath)))
                     .OrderByDescending(static candidate => candidate.commandPath.Length)
                     .ThenBy(static candidate => candidate.operation.OperationId, StringComparer.Ordinal))
        {
            builder.Append("        if (commandTokens.Length >= ")
                .Append(commandPath.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine(")");
            builder.AppendLine("        {");
            builder.Append("            if (");
            for (int index = 0; index < commandPath.Length; index++)
            {
                if (index > 0)
                    builder.AppendLine().Append("                && ");

                builder.Append('(')
                    .Append("commandTokens[")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("] == ")
                    .Append(ToLiteral(commandPath[index]))
                    .Append(" || global::System.String.Equals(commandTokens[")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("], ")
                    .Append(ToLiteral(commandPath[index]))
                    .Append(", global::System.StringComparison.OrdinalIgnoreCase))");
            }

            builder.AppendLine(")");
            builder.AppendLine("            {");
            builder.Append("                if (global::Manifold.Cli.CliBinding.ContainsReservedGlobalFlag(commandTokens, ")
                .Append(commandPath.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine("))");
            builder.AppendLine("                {");
            builder.AppendLine("                    invocation = default;");
            builder.AppendLine("                    return false;");
            builder.AppendLine("                }");
            builder.AppendLine();
            builder.Append("                invocation = Invoke")
                .Append(GetOperationMethodBaseName(operation.OperationId))
                .Append("FastAsync(commandTokens, ")
                .Append(commandPath.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine(", services, cancellationToken);");
            builder.AppendLine("                return true;");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
        }

        builder.AppendLine();
        builder.AppendLine("        invocation = default;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public bool TryInvoke(");
        builder.AppendLine("        string operationId,");
        builder.AppendLine("        global::System.Collections.Generic.IReadOnlyDictionary<string, string> options,");
        builder.AppendLine("        global::System.Collections.Generic.IReadOnlyList<string> arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        bool jsonRequested,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::System.Threading.Tasks.ValueTask<global::Manifold.Cli.CliInvocationResult> invocation)");
        builder.AppendLine("    {");
        foreach (OperationCandidate operation in operations.Where(static operation => operation.Visibility is not OperationVisibilityCandidate.McpOnly))
        {
            builder.Append("        if (global::System.String.Equals(operationId, ")
                .Append(ToLiteral(operation.OperationId))
                .AppendLine(", global::System.StringComparison.Ordinal))");
            builder.AppendLine("        {");
            builder.Append("            invocation = Invoke")
                .Append(GetOperationMethodBaseName(operation.OperationId))
                .AppendLine("Async(options, arguments, services, cancellationToken);");
            builder.AppendLine("            return true;");
            builder.AppendLine("        }");
        }

        builder.AppendLine();
        builder.AppendLine("        invocation = default;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();

        foreach (OperationCandidate operation in operations.Where(static operation => operation.Visibility is not OperationVisibilityCandidate.McpOnly))
            AppendCliInvoker(builder, operation);

        foreach (OperationCandidate operation in operations.Where(static operation =>
                     operation.Visibility is not OperationVisibilityCandidate.McpOnly &&
                     CanUseFastCliInvoker(operation)))
        {
            if (CanUseFastSyncCliInvoker(operation))
                AppendFastSyncCliInvoker(builder, operation);
            AppendFastCliInvoker(builder, operation);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendOperationInvoker(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        builder.Append("    private static ");
        builder.Append(operation.ReturnKind is MethodReturnKind.TaskOfT or MethodReturnKind.ValueTaskOfT or MethodReturnKind.Task or MethodReturnKind.ValueTask
            ? "async "
            : string.Empty);
        builder.AppendLine("global::System.Threading.Tasks.ValueTask<global::Manifold.OperationInvocationResult> Invoke" + methodBaseName + "Async(");
        builder.AppendLine("        object? request,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::Manifold.InvocationSurface surface,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        if (string.IsNullOrWhiteSpace(operation.RequestTypeName))
            throw new InvalidOperationException("Generated operation invoker requires a request type.");

        builder.Append("        ").Append(operation.RequestTypeName).AppendLine(" __request;");
        builder.AppendLine("        switch (request)");
        builder.AppendLine("        {");
        builder.AppendLine("            case null:");
        builder.Append("                __request = new ").Append(operation.RequestTypeName).AppendLine("();");
        builder.AppendLine("                break;");
        builder.Append("            case ").Append(operation.RequestTypeName).AppendLine(" typedRequest:");
        builder.AppendLine("                __request = typedRequest;");
        builder.AppendLine("                break;");
        builder.AppendLine("            default:");
        builder.Append("                throw new global::System.ArgumentException(")
            .Append(ToLiteral(
                $"Operation '{operation.OperationId}' expected a request instance of type '{operation.RequestTypeName}'."))
            .AppendLine(");");
        builder.AppendLine("        }");

        builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.OperationBinding.GetRequiredService<")
            .Append(operation.DeclaringTypeName).AppendLine(">(services);");
        string invocationExpression = "__operation." + operation.MethodName + "(__request, new global::Manifold.OperationContext(" +
                                   ToLiteral(operation.OperationId) +
                                   ", surface, services, cancellationToken: cancellationToken))";

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendOperationResultReturn(builder, operation, wrapInValueTask: true);
                break;
            case MethodReturnKind.TaskOfT:
            case MethodReturnKind.ValueTaskOfT:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = await ").Append(invocationExpression)
                    .AppendLine(".ConfigureAwait(false);");
                AppendOperationResultReturn(builder, operation, wrapInValueTask: false);
                break;
            default:
                throw new InvalidOperationException("Generated operation invoker only supports operations with results.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static string GenerateMcpToolsSource(IReadOnlyList<OperationCandidate> operations)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Manifold.Generated;");
        builder.AppendLine();
        builder.AppendLine("[global::ModelContextProtocol.Server.McpServerToolType]");
        builder.AppendLine("public sealed class GeneratedMcpTools");
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::System.IServiceProvider? services;");
        builder.AppendLine();
        builder.AppendLine("    public GeneratedMcpTools(global::System.IServiceProvider? services = null)");
        builder.AppendLine("    {");
        builder.AppendLine("        this.services = services;");
        builder.AppendLine("    }");
        builder.AppendLine();
        foreach (OperationCandidate operation in operations.Where(static operation =>
                     operation.Visibility is not OperationVisibilityCandidate.CliOnly &&
                     !string.IsNullOrWhiteSpace(operation.McpToolName)))
        {
            AppendMcpToolMethod(builder, operation);
        }

        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("public static class GeneratedMcpServiceCollectionExtensions");
        builder.AppendLine("{");
        builder.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IMcpServerBuilder AddGeneratedMcpServer(");
        builder.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(services);");
        builder.AppendLine();
        builder.AppendLine("        global::Microsoft.Extensions.DependencyInjection.IMcpServerBuilder builder =");
        builder.AppendLine("            global::Microsoft.Extensions.DependencyInjection.McpServerServiceCollectionExtensions.AddMcpServer(services, static _ => { });");
        builder.AppendLine("        return global::Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.WithTools<GeneratedMcpTools>(builder);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateMcpCatalogSource(IReadOnlyList<OperationCandidate> operations)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Manifold.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class GeneratedMcpCatalog");
        builder.AppendLine("{");
        builder.AppendLine("    private static readonly global::Manifold.Mcp.McpToolDescriptor[] tools =");
        builder.AppendLine("    [");
        foreach (OperationCandidate operation in operations.Where(static operation =>
                     operation.Visibility is not OperationVisibilityCandidate.CliOnly &&
                     !string.IsNullOrWhiteSpace(operation.McpToolName)))
        {
            AppendMcpToolDescriptor(builder, operation);
        }

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    private static readonly global::System.Collections.Generic.Dictionary<string, global::Manifold.Mcp.McpToolDescriptor> toolsByName = CreateToolsByName();");
        builder.AppendLine();
        builder.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::Manifold.Mcp.McpToolDescriptor> Tools => tools;");
        builder.AppendLine();
        builder.AppendLine("    public static global::System.ReadOnlySpan<global::Manifold.Mcp.McpToolDescriptor> AsSpan()");
        builder.AppendLine("    {");
        builder.AppendLine("        return tools;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static bool TryFind(string toolName, out global::Manifold.Mcp.McpToolDescriptor descriptor)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(toolName);");
        builder.AppendLine("        return toolsByName.TryGetValue(toolName, out descriptor);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static global::System.Collections.Generic.Dictionary<string, global::Manifold.Mcp.McpToolDescriptor> CreateToolsByName()");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.Collections.Generic.Dictionary<string, global::Manifold.Mcp.McpToolDescriptor> map = new(tools.Length, global::System.StringComparer.Ordinal);");
        builder.AppendLine("        foreach (global::Manifold.Mcp.McpToolDescriptor tool in tools)");
        builder.AppendLine("            map.Add(tool.Name, tool);");
        builder.AppendLine();
        builder.AppendLine("        return map;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateMcpInvokerSource(IReadOnlyList<OperationCandidate> operations)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Manifold.Generated;");
        builder.AppendLine();
        builder.AppendLine("public sealed class GeneratedMcpInvoker : global::Manifold.Mcp.IMcpToolInvoker, global::Manifold.Mcp.IFastMcpToolInvoker, global::Manifold.Mcp.IFastSyncMcpToolInvoker");
        builder.AppendLine("{");
        builder.AppendLine("    public bool TryInvokeFastSync(");
        builder.AppendLine("        string toolName,");
        builder.AppendLine("        global::System.Text.Json.JsonElement? arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::Manifold.Mcp.FastMcpInvocationResult invocation)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(toolName);");
        builder.AppendLine();
        AppendMcpSwitchDispatch(
            builder,
            operations.Where(static operation =>
                operation.Visibility is not OperationVisibilityCandidate.CliOnly &&
                !string.IsNullOrWhiteSpace(operation.McpToolName) &&
                CanUseFastSyncMcpInvoker(operation)),
            "invocation = Invoke{0}FastSync(arguments, services, cancellationToken);",
            "invocation = default;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public bool TryInvokeFast(");
        builder.AppendLine("        string toolName,");
        builder.AppendLine("        global::System.Text.Json.JsonElement? arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::System.Threading.Tasks.ValueTask<global::Manifold.Mcp.FastMcpInvocationResult> invocation)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(toolName);");
        builder.AppendLine();
        AppendMcpSwitchDispatch(
            builder,
            operations.Where(static operation =>
                operation.Visibility is not OperationVisibilityCandidate.CliOnly &&
                !string.IsNullOrWhiteSpace(operation.McpToolName)),
            "invocation = Invoke{0}FastAsync(arguments, services, cancellationToken);",
            "invocation = default;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public bool TryInvoke(");
        builder.AppendLine("        string toolName,");
        builder.AppendLine("        global::System.Text.Json.JsonElement? arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken,");
        builder.AppendLine("        out global::System.Threading.Tasks.ValueTask<global::Manifold.OperationInvocationResult> invocation)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(toolName);");
        builder.AppendLine();
        AppendMcpSwitchDispatch(
            builder,
            operations.Where(static operation =>
                operation.Visibility is not OperationVisibilityCandidate.CliOnly &&
                !string.IsNullOrWhiteSpace(operation.McpToolName)),
            "invocation = Invoke{0}Async(arguments, services, cancellationToken);",
            "invocation = default;");
        builder.AppendLine("    }");
        builder.AppendLine();
        foreach (OperationCandidate operation in operations.Where(static operation =>
                     operation.Visibility is not OperationVisibilityCandidate.CliOnly &&
                     !string.IsNullOrWhiteSpace(operation.McpToolName)))
        {
            AppendMcpInvokerMethod(builder, operation);
            AppendFastMcpInvokerMethod(builder, operation);
            if (CanUseFastSyncMcpInvoker(operation))
                AppendFastSyncMcpInvokerMethod(builder, operation);
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendCliInvoker(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        builder.Append("    private static ");
        builder.Append(operation.ReturnKind is MethodReturnKind.TaskOfT or MethodReturnKind.ValueTaskOfT or MethodReturnKind.Task or MethodReturnKind.ValueTask
            ? "async "
            : string.Empty);
        builder.AppendLine("global::System.Threading.Tasks.ValueTask<global::Manifold.Cli.CliInvocationResult> Invoke" + methodBaseName + "Async(");
        builder.AppendLine("        global::System.Collections.Generic.IReadOnlyDictionary<string, string> options,");
        builder.AppendLine("        global::System.Collections.Generic.IReadOnlyList<string> arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        foreach (ParameterCandidate parameter in operation.Parameters)
            AppendCliParameterBinding(builder, parameter);

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetBoundVariableName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Cli.CliBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForCli(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: cancellationToken))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetBoundVariableName(parameter))) + ")";
        }

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.Append("        return global::System.Threading.Tasks.ValueTask.FromResult(new global::Manifold.Cli.CliInvocationResult(null, typeof(")
                    .Append(operation.ResultTypeName).AppendLine("), null));");
                break;
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendCliResultReturn(builder, operation);
                break;
            case MethodReturnKind.Task:
                builder.Append("        await ").Append(invocationExpression).AppendLine(".ConfigureAwait(false);");
                builder.Append("        return new global::Manifold.Cli.CliInvocationResult(null, typeof(")
                    .Append(operation.ResultTypeName).AppendLine("), null);");
                break;
            case MethodReturnKind.TaskOfT:
            case MethodReturnKind.ValueTaskOfT:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = await ").Append(invocationExpression)
                    .AppendLine(".ConfigureAwait(false);");
                AppendCliResultReturn(builder, operation);
                break;
            case MethodReturnKind.ValueTask:
                builder.Append("        await ").Append(invocationExpression).AppendLine(".ConfigureAwait(false);");
                builder.Append("        return new global::Manifold.Cli.CliInvocationResult(null, typeof(")
                    .Append(operation.ResultTypeName).AppendLine("), null);");
                break;
            default:
                throw new InvalidOperationException("Unsupported return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendFastSyncCliInvoker(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        string commandDisplay = operation.CliCommandPath is { } cliCommandPath && !cliCommandPath.IsDefaultOrEmpty
            ? string.Join(" ", cliCommandPath)
            : operation.OperationId;
        ParameterCandidate[] argumentParameters = operation.Parameters
            .Where(static parameter => parameter.Source == ParameterSourceCandidate.Argument)
            .OrderBy(static parameter => parameter.Position)
            .ToArray();
        ParameterCandidate[] optionParameters = operation.Parameters
            .Where(static parameter => parameter.Source == ParameterSourceCandidate.Option)
            .ToArray();
        (ParameterCandidate Parameter, string OptionName, string ExactToken, string InlinePrefix)[] optionVariants = optionParameters
            .SelectMany(static parameter => GetCliOptionNames(parameter)
                .Select(optionName => (parameter, optionName, "--" + optionName, "--" + optionName + "=")))
            .ToArray();

        builder.AppendLine("    private static global::Manifold.Cli.FastCliInvocationResult Invoke" + methodBaseName + "FastSync(");
        builder.AppendLine("        string[] commandTokens,");
        builder.AppendLine("        int startIndex,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        foreach (ParameterCandidate parameter in operation.Parameters.Where(static parameter =>
                     parameter.Source is ParameterSourceCandidate.Argument or ParameterSourceCandidate.Option))
        {
            string boundName = GetBoundVariableName(parameter);
            builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                .Append(" = ").Append(GetFastCliDefaultExpression(parameter.ParameterTypeName)).AppendLine(";");
            if (parameter.Required)
                builder.Append("        bool ").Append(boundName).AppendLine("Set = false;");
        }

        if (argumentParameters.Length > 0)
            builder.AppendLine("        int argumentIndex = 0;");

        builder.AppendLine("        for (int index = startIndex; index < commandTokens.Length; index++)");
        builder.AppendLine("        {");
        builder.AppendLine("            string current = commandTokens[index];");

        if (optionParameters.Length > 0)
        {
            builder.AppendLine("            if (current.Length > 1 && current[0] == '-' && current[1] == '-')");
            builder.AppendLine("            {");
            builder.AppendLine("                char optionDiscriminator = current.Length > 2");
            builder.AppendLine("                    ? (char)(current[2] | (char)0x20)");
            builder.AppendLine("                    : '\\0';");
            builder.AppendLine("                switch (optionDiscriminator)");
            builder.AppendLine("                {");
            foreach (IGrouping<char, (ParameterCandidate Parameter, string OptionName, string ExactToken, string InlinePrefix)> optionGroup in optionVariants
                         .GroupBy(static variant => char.ToLowerInvariant(variant.ExactToken[2]))
                         .OrderBy(static group => group.Key))
            {
                builder.Append("                    case '").Append(optionGroup.Key).AppendLine("':");
                foreach ((ParameterCandidate parameter, string optionName, string exactToken, string inlinePrefix) in optionGroup)
                {
                    string boundName = GetBoundVariableName(parameter);
                    string displayName = parameter.CliName ?? parameter.Name;
                    string valueLocalName = boundName + "Value";

                    builder.Append("                        if (current == ")
                        .Append(ToLiteral(exactToken))
                        .Append(" || global::System.String.Equals(current, ")
                        .Append(ToLiteral(exactToken))
                        .AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
                    builder.AppendLine("                        {");
                    builder.Append("                            string ").Append(valueLocalName)
                        .Append(" = global::Manifold.Cli.CliBinding.ParseRequiredOptionValue(commandTokens, ref index, ")
                        .Append(ToLiteral(optionName)).AppendLine(");");
                    builder.Append("                            ").Append(boundName).Append(" = ")
                        .Append(GetFastCliParseExpression(parameter, valueLocalName, displayName))
                        .AppendLine(";");
                    if (parameter.Required)
                        builder.Append("                            ").Append(boundName).AppendLine("Set = true;");
                    builder.AppendLine("                            continue;");
                    builder.AppendLine("                        }");
                    builder.Append("                        if (current.StartsWith(")
                        .Append(ToLiteral(inlinePrefix))
                        .AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
                    builder.AppendLine("                        {");
                    builder.Append("                            string ").Append(valueLocalName).Append(" = current.Substring(")
                        .Append(inlinePrefix.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
                    builder.Append("                            if (global::System.String.IsNullOrWhiteSpace(").Append(valueLocalName).AppendLine("))");
                    builder.Append("                                throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"The --{optionName} option requires a non-empty value.")).AppendLine(");");
                    builder.Append("                            ").Append(boundName).Append(" = ")
                        .Append(GetFastCliParseExpression(parameter, valueLocalName, displayName))
                        .AppendLine(";");
                    if (parameter.Required)
                        builder.Append("                            ").Append(boundName).AppendLine("Set = true;");
                    builder.AppendLine("                            continue;");
                    builder.AppendLine("                        }");
                }

                builder.AppendLine("                        break;");
            }

            builder.AppendLine("                }");
            builder.Append("                throw new global::System.ArgumentException(")
                .Append(ToLiteral($"Unknown option '{{current}}' for command '{commandDisplay}'."))
                .AppendLine(".Replace(\"{current}\", current));");
            builder.AppendLine("            }");
        }

        if (argumentParameters.Length > 0)
        {
            builder.AppendLine("            switch (argumentIndex)");
            builder.AppendLine("            {");
            foreach (ParameterCandidate parameter in argumentParameters)
            {
                string boundName = GetBoundVariableName(parameter);
                string displayName = parameter.CliName ?? parameter.Name;
                builder.Append("                case ")
                    .Append(parameter.Position!.Value.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(":");
                builder.Append("                    ").Append(boundName).Append(" = ")
                    .Append(GetFastCliParseExpression(parameter, "current", displayName)).AppendLine(";");
                if (parameter.Required)
                    builder.Append("                    ").Append(boundName).AppendLine("Set = true;");
                builder.AppendLine("                    argumentIndex++;");
                builder.AppendLine("                    continue;");
            }

            builder.AppendLine("                default:");
            builder.Append("                    throw new global::System.ArgumentException(")
                .Append(ToLiteral($"Unexpected argument '{{current}}' for command '{commandDisplay}'."))
                .AppendLine(".Replace(\"{current}\", current));");
            builder.AppendLine("            }");
        }
        else
        {
            builder.Append("            throw new global::System.ArgumentException(")
                .Append(ToLiteral($"Unexpected argument '{{current}}' for command '{commandDisplay}'."))
                .AppendLine(".Replace(\"{current}\", current));");
        }

        builder.AppendLine("        }");

        foreach (ParameterCandidate parameter in operation.Parameters.Where(static parameter => parameter.Required))
        {
            string boundName = GetBoundVariableName(parameter);
            switch (parameter.Source)
            {
                case ParameterSourceCandidate.Argument:
                    builder.Append("        if (!").Append(boundName).AppendLine("Set)");
                    builder.Append("            throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"Missing required argument '{parameter.CliName ?? parameter.Name}'."))
                        .AppendLine(");");
                    break;
                case ParameterSourceCandidate.Option:
                    builder.Append("        if (!").Append(boundName).AppendLine("Set)");
                    builder.Append("            throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"Missing required --{parameter.CliName ?? parameter.Name} option."))
                        .AppendLine(");");
                    break;
            }
        }

        foreach (ParameterCandidate parameter in operation.Parameters)
        {
            if (parameter.Source == ParameterSourceCandidate.CancellationToken)
            {
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(GetBoundVariableName(parameter))
                    .AppendLine(" = cancellationToken;");
            }
            else if (parameter.Source == ParameterSourceCandidate.Service)
            {
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(GetBoundVariableName(parameter))
                    .Append(" = global::Manifold.Cli.CliBinding.GetRequiredService<").Append(parameter.ParameterTypeName)
                    .AppendLine(">(services);");
            }
        }

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetBoundVariableName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Cli.CliBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForCli(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: cancellationToken))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetBoundVariableName(parameter))) + ")";
        }

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.AppendLine("        return global::Manifold.Cli.FastCliInvocationResult.None;");
                break;
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendFastCliResultReturn(builder, operation, wrapInValueTask: false);
                break;
            default:
                throw new InvalidOperationException("Unsupported fast sync CLI return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendFastCliInvoker(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        string commandDisplay = operation.CliCommandPath is { } cliCommandPath && !cliCommandPath.IsDefaultOrEmpty
            ? string.Join(" ", cliCommandPath)
            : operation.OperationId;
        ParameterCandidate[] argumentParameters = operation.Parameters
            .Where(static parameter => parameter.Source == ParameterSourceCandidate.Argument)
            .OrderBy(static parameter => parameter.Position)
            .ToArray();
        ParameterCandidate[] optionParameters = operation.Parameters
            .Where(static parameter => parameter.Source == ParameterSourceCandidate.Option)
            .ToArray();

        builder.Append("    private static ");
        builder.Append(operation.ReturnKind is MethodReturnKind.TaskOfT or MethodReturnKind.ValueTaskOfT or MethodReturnKind.Task or MethodReturnKind.ValueTask
            ? "async "
            : string.Empty);
        builder.AppendLine("global::System.Threading.Tasks.ValueTask<global::Manifold.Cli.FastCliInvocationResult> Invoke" + methodBaseName + "FastAsync(");
        builder.AppendLine("        string[] commandTokens,");
        builder.AppendLine("        int startIndex,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        foreach (ParameterCandidate parameter in operation.Parameters.Where(static parameter =>
                     parameter.Source is ParameterSourceCandidate.Argument or ParameterSourceCandidate.Option))
        {
            string boundName = GetBoundVariableName(parameter);
            builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                .Append(" = ").Append(GetFastCliDefaultExpression(parameter.ParameterTypeName)).AppendLine(";");
            if (parameter.Required)
                builder.Append("        bool ").Append(boundName).AppendLine("Set = false;");
        }

        if (argumentParameters.Length > 0)
            builder.AppendLine("        int argumentIndex = 0;");

        builder.AppendLine("        for (int index = startIndex; index < commandTokens.Length; index++)");
        builder.AppendLine("        {");
        builder.AppendLine("            string current = commandTokens[index];");

        if (optionParameters.Length > 0)
        {
            builder.AppendLine("            if (current.StartsWith(\"--\", global::System.StringComparison.Ordinal))");
            builder.AppendLine("            {");
            foreach (ParameterCandidate parameter in optionParameters)
            {
                foreach (string optionName in GetCliOptionNames(parameter))
                {
                    string exactToken = "--" + optionName;
                    string boundName = GetBoundVariableName(parameter);
                    string displayName = parameter.CliName ?? parameter.Name;
                    string valueLocalName = boundName + "Value";

                    builder.Append("                if (current == ")
                        .Append(ToLiteral(exactToken))
                        .Append(" || global::System.String.Equals(current, ")
                        .Append(ToLiteral(exactToken))
                        .AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
                    builder.AppendLine("                {");
                    builder.Append("                    string ").Append(valueLocalName)
                        .Append(" = global::Manifold.Cli.CliBinding.ParseRequiredOptionValue(commandTokens, ref index, ")
                        .Append(ToLiteral(optionName)).AppendLine(");");
                    builder.Append("                    ").Append(boundName).Append(" = ")
                        .Append(GetFastCliParseExpression(parameter, valueLocalName, displayName))
                        .AppendLine(";");
                    if (parameter.Required)
                        builder.Append("                    ").Append(boundName).AppendLine("Set = true;");
                    builder.AppendLine("                    continue;");
                    builder.AppendLine("                }");
                }
            }

            foreach (ParameterCandidate parameter in optionParameters)
            {
                foreach (string optionName in GetCliOptionNames(parameter))
                {
                    string exactToken = "--" + optionName;
                    string inlinePrefix = exactToken + "=";
                    string boundName = GetBoundVariableName(parameter);
                    string displayName = parameter.CliName ?? parameter.Name;
                    string valueLocalName = boundName + "Value";

                    builder.Append("                if (current.StartsWith(")
                        .Append(ToLiteral(inlinePrefix))
                        .AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
                    builder.AppendLine("                {");
                    builder.Append("                    string ").Append(valueLocalName).Append(" = current.Substring(")
                        .Append(inlinePrefix.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
                    builder.Append("                    if (global::System.String.IsNullOrWhiteSpace(").Append(valueLocalName).AppendLine("))");
                    builder.Append("                        throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"The --{optionName} option requires a non-empty value.")).AppendLine(");");
                    builder.Append("                    ").Append(boundName).Append(" = ")
                        .Append(GetFastCliParseExpression(parameter, valueLocalName, displayName))
                        .AppendLine(";");
                    if (parameter.Required)
                        builder.Append("                    ").Append(boundName).AppendLine("Set = true;");
                    builder.AppendLine("                    continue;");
                    builder.AppendLine("                }");
                }
            }

            builder.Append("                throw new global::System.ArgumentException(")
                .Append(ToLiteral($"Unknown option '{{current}}' for command '{commandDisplay}'."))
                .AppendLine(".Replace(\"{current}\", current));");
            builder.AppendLine("            }");
        }

        if (argumentParameters.Length > 0)
        {
            builder.AppendLine("            switch (argumentIndex)");
            builder.AppendLine("            {");
            foreach (ParameterCandidate parameter in argumentParameters)
            {
                string boundName = GetBoundVariableName(parameter);
                string displayName = parameter.CliName ?? parameter.Name;
                builder.Append("                case ")
                    .Append(parameter.Position!.Value.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(":");
                builder.Append("                    ").Append(boundName).Append(" = ")
                    .Append(GetFastCliParseExpression(parameter, "current", displayName)).AppendLine(";");
                if (parameter.Required)
                    builder.Append("                    ").Append(boundName).AppendLine("Set = true;");
                builder.AppendLine("                    argumentIndex++;");
                builder.AppendLine("                    continue;");
            }

            builder.AppendLine("                default:");
            builder.Append("                    throw new global::System.ArgumentException(")
                .Append(ToLiteral($"Unexpected argument '{{current}}' for command '{commandDisplay}'."))
                .AppendLine(".Replace(\"{current}\", current));");
            builder.AppendLine("            }");
        }
        else
        {
            builder.Append("            throw new global::System.ArgumentException(")
                .Append(ToLiteral($"Unexpected argument '{{current}}' for command '{commandDisplay}'."))
                .AppendLine(".Replace(\"{current}\", current));");
        }

        builder.AppendLine("        }");

        foreach (ParameterCandidate parameter in operation.Parameters.Where(static parameter => parameter.Required))
        {
            string boundName = GetBoundVariableName(parameter);
            switch (parameter.Source)
            {
                case ParameterSourceCandidate.Argument:
                    builder.Append("        if (!").Append(boundName).AppendLine("Set)");
                    builder.Append("            throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"Missing required argument '{parameter.CliName ?? parameter.Name}'."))
                        .AppendLine(");");
                    break;
                case ParameterSourceCandidate.Option:
                    builder.Append("        if (!").Append(boundName).AppendLine("Set)");
                    builder.Append("            throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"Missing required --{parameter.CliName ?? parameter.Name} option."))
                        .AppendLine(");");
                    break;
            }
        }

        foreach (ParameterCandidate parameter in operation.Parameters)
        {
            if (parameter.Source == ParameterSourceCandidate.CancellationToken)
            {
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(GetBoundVariableName(parameter))
                    .AppendLine(" = cancellationToken;");
            }
            else if (parameter.Source == ParameterSourceCandidate.Service)
            {
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(GetBoundVariableName(parameter))
                    .Append(" = global::Manifold.Cli.CliBinding.GetRequiredService<").Append(parameter.ParameterTypeName)
                    .AppendLine(">(services);");
            }
        }

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetBoundVariableName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Cli.CliBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForCli(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: cancellationToken))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetBoundVariableName(parameter))) + ")";
        }

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.AppendLine("        return global::System.Threading.Tasks.ValueTask.FromResult(global::Manifold.Cli.FastCliInvocationResult.None);");
                break;
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendFastCliResultReturn(builder, operation, wrapInValueTask: true);
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.ValueTask:
                builder.Append("        await ").Append(invocationExpression).AppendLine(".ConfigureAwait(false);");
                builder.AppendLine("        return global::Manifold.Cli.FastCliInvocationResult.None;");
                break;
            case MethodReturnKind.TaskOfT:
            case MethodReturnKind.ValueTaskOfT:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = await ").Append(invocationExpression)
                    .AppendLine(".ConfigureAwait(false);");
                AppendFastCliResultReturn(builder, operation, wrapInValueTask: false);
                break;
            default:
                throw new InvalidOperationException("Unsupported return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendMcpInvokerMethod(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        builder.Append("    private static ");
        builder.Append(operation.ReturnKind is MethodReturnKind.TaskOfT or MethodReturnKind.ValueTaskOfT or MethodReturnKind.Task or MethodReturnKind.ValueTask
            ? "async "
            : string.Empty);
        builder.AppendLine("global::System.Threading.Tasks.ValueTask<global::Manifold.OperationInvocationResult> Invoke" + methodBaseName + "Async(");
        builder.AppendLine("        global::System.Text.Json.JsonElement? arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        AppendMcpArgumentObjectInitialization(builder, operation);
        AppendMcpInvokerParameterBindings(builder, operation);

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetBoundVariableName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Mcp.McpBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForMcp(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: cancellationToken))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetBoundVariableName(parameter))) + ")";
        }

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.AppendLine("        return global::System.Threading.Tasks.ValueTask.FromResult(new global::Manifold.OperationInvocationResult(null, typeof(void), null));");
                break;
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendOperationResultReturn(builder, operation, wrapInValueTask: true);
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.ValueTask:
                builder.Append("        await ").Append(invocationExpression).AppendLine(".ConfigureAwait(false);");
                builder.AppendLine("        return new global::Manifold.OperationInvocationResult(null, typeof(void), null);");
                break;
            case MethodReturnKind.TaskOfT:
            case MethodReturnKind.ValueTaskOfT:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = await ").Append(invocationExpression)
                    .AppendLine(".ConfigureAwait(false);");
                AppendOperationResultReturn(builder, operation, wrapInValueTask: false);
                break;
            default:
                throw new InvalidOperationException("Unsupported MCP invoker return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendFastMcpInvokerMethod(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        builder.Append("    private static ");
        builder.Append(operation.ReturnKind is MethodReturnKind.TaskOfT or MethodReturnKind.ValueTaskOfT or MethodReturnKind.Task or MethodReturnKind.ValueTask
            ? "async "
            : string.Empty);
        builder.AppendLine("global::System.Threading.Tasks.ValueTask<global::Manifold.Mcp.FastMcpInvocationResult> Invoke" + methodBaseName + "FastAsync(");
        builder.AppendLine("        global::System.Text.Json.JsonElement? arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        AppendMcpArgumentObjectInitialization(builder, operation);
        AppendMcpInvokerParameterBindings(builder, operation);

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetBoundVariableName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Mcp.McpBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForMcp(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: cancellationToken))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetBoundVariableName(parameter))) + ")";
        }

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.AppendLine("        return global::System.Threading.Tasks.ValueTask.FromResult(global::Manifold.Mcp.FastMcpInvocationResult.None);");
                break;
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendFastMcpResultReturn(builder, operation, wrapInValueTask: true);
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.ValueTask:
                builder.Append("        await ").Append(invocationExpression).AppendLine(".ConfigureAwait(false);");
                builder.AppendLine("        return global::Manifold.Mcp.FastMcpInvocationResult.None;");
                break;
            case MethodReturnKind.TaskOfT:
            case MethodReturnKind.ValueTaskOfT:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = await ").Append(invocationExpression)
                    .AppendLine(".ConfigureAwait(false);");
                AppendFastMcpResultReturn(builder, operation, wrapInValueTask: false);
                break;
            default:
                throw new InvalidOperationException("Unsupported fast MCP invoker return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendFastSyncMcpInvokerMethod(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        builder.AppendLine("    private static global::Manifold.Mcp.FastMcpInvocationResult Invoke" + methodBaseName + "FastSync(");
        builder.AppendLine("        global::System.Text.Json.JsonElement? arguments,");
        builder.AppendLine("        global::System.IServiceProvider? services,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");

        AppendMcpArgumentObjectInitialization(builder, operation);
        AppendMcpInvokerParameterBindings(builder, operation);

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetBoundVariableName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Mcp.McpBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForMcp(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: cancellationToken))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetBoundVariableName(parameter))) + ")";
        }

        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.AppendLine("        return global::Manifold.Mcp.FastMcpInvocationResult.None;");
                break;
            case MethodReturnKind.Value:
                builder.Append("        ").Append(operation.ResultTypeName).Append(" result = ").Append(invocationExpression).AppendLine(";");
                AppendFastMcpResultReturn(builder, operation, wrapInValueTask: false);
                break;
            default:
                throw new InvalidOperationException("Unsupported fast sync MCP invoker return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendMcpToolMethod(StringBuilder builder, OperationCandidate operation)
    {
        string methodBaseName = GetOperationMethodBaseName(operation.OperationId);
        builder.Append("    [global::ModelContextProtocol.Server.McpServerTool(Name = ")
            .Append(ToLiteral(operation.McpToolName!))
            .AppendLine(")]");
        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            builder.Append("    [global::System.ComponentModel.Description(")
                .Append(ToLiteral(operation.Description!))
                .AppendLine(")]");
        }

        builder.Append("    public ");
        builder.Append(GetGeneratedMcpReturnType(operation));
        builder.Append(' ');
        builder.Append(methodBaseName);
        builder.Append("Async(");
        AppendMcpParameterList(builder, operation);
        builder.AppendLine(")");
        builder.AppendLine("    {");

        foreach (ParameterCandidate parameter in operation.Parameters.Where(static parameter =>
                     parameter.Source == ParameterSourceCandidate.Service))
        {
            builder.Append("        ")
                .Append(parameter.ParameterTypeName)
                .Append(' ')
                .Append(GetBoundVariableName(parameter))
                .Append(" = global::Manifold.Mcp.McpBinding.GetRequiredService<")
                .Append(parameter.ParameterTypeName)
                .AppendLine(">(services);");
        }

        string invocationExpression;
        if (operation.InvocationKind == InvocationKind.InstanceOperation)
        {
            AppendRequestBinding(builder, operation, "__request", static parameter => GetInvocationArgumentName(parameter));
            builder.Append("        ").Append(operation.DeclaringTypeName).Append(" __operation = global::Manifold.Mcp.McpBinding.GetRequiredServiceOrThrow<")
                .Append(operation.DeclaringTypeName).AppendLine(">(services);");
            string cancellationTokenExpression = operation.Parameters.FirstOrDefault(static parameter =>
                    parameter.Source == ParameterSourceCandidate.CancellationToken) is { } cancellationTokenParameter
                ? GetMcpParameterName(cancellationTokenParameter)
                : "global::System.Threading.CancellationToken.None";
            invocationExpression = "__operation." + operation.MethodName + "(__request, global::Manifold.OperationContext.ForMcp(" +
                                   ToLiteral(operation.OperationId) +
                                   ", services, cancellationToken: " + cancellationTokenExpression + "))";
        }
        else
        {
            invocationExpression = operation.DeclaringTypeName + "." + operation.MethodName + "(" +
                                   string.Join(", ", operation.Parameters.Select(static parameter => GetInvocationArgumentName(parameter))) + ")";
        }
        switch (operation.ReturnKind)
        {
            case MethodReturnKind.Void:
                builder.Append("        ").Append(invocationExpression).AppendLine(";");
                builder.AppendLine("        return global::System.Threading.Tasks.Task.CompletedTask;");
                break;
            case MethodReturnKind.Value:
                builder.Append("        return global::System.Threading.Tasks.Task.FromResult(")
                    .Append(invocationExpression)
                    .AppendLine(");");
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.TaskOfT:
                builder.Append("        return ").Append(invocationExpression).AppendLine(";");
                break;
            case MethodReturnKind.ValueTask:
                builder.Append("        return ").Append(invocationExpression).AppendLine(".AsTask();");
                break;
            case MethodReturnKind.ValueTaskOfT:
                builder.Append("        return ").Append(invocationExpression).AppendLine(".AsTask();");
                break;
            default:
                throw new InvalidOperationException("Unsupported MCP return kind.");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendMcpSwitchDispatch(
        StringBuilder builder,
        IEnumerable<OperationCandidate> operations,
        string invocationFormat,
        string defaultAssignment)
    {
        builder.AppendLine("        switch (toolName)");
        builder.AppendLine("        {");
        foreach (OperationCandidate operation in operations.OrderBy(static operation => operation.McpToolName, StringComparer.Ordinal))
        {
            builder.Append("            case ")
                .Append(ToLiteral(operation.McpToolName!))
                .AppendLine(":");
            builder.Append("                ")
                .Append(string.Format(CultureInfo.InvariantCulture, invocationFormat, GetOperationMethodBaseName(operation.OperationId)))
                .AppendLine();
            builder.AppendLine("                return true;");
        }

        builder.AppendLine("            default:");
        builder.Append("                ").Append(defaultAssignment).AppendLine();
        builder.AppendLine("                return false;");
        builder.AppendLine("        }");
    }

    private static void AppendMcpArgumentObjectInitialization(StringBuilder builder, OperationCandidate operation)
    {
        bool hasBindableParameters = operation.Parameters.Any(static parameter =>
            parameter.Source is ParameterSourceCandidate.Argument or ParameterSourceCandidate.Option);
        if (!hasBindableParameters)
            return;

        builder.AppendLine("        bool __uops_internal_mcpHasArgumentObject = global::Manifold.Mcp.McpBinding.TryGetObject(arguments, out global::System.Text.Json.JsonElement __uops_internal_mcpArgumentObject);");
        builder.AppendLine();
    }

    private static void AppendMcpInvokerParameterBindings(StringBuilder builder, OperationCandidate operation)
    {
        foreach (ParameterCandidate parameter in operation.Parameters)
        {
            AppendMcpInvokerParameterBinding(builder, parameter);
        }
    }

    private static void AppendMcpInvokerParameterBinding(StringBuilder builder, ParameterCandidate parameter)
    {
        string boundName = GetBoundVariableName(parameter);
        string displayName = parameter.McpName ?? parameter.Name;
        switch (parameter.Source)
        {
            case ParameterSourceCandidate.CancellationToken:
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                    .AppendLine(" = cancellationToken;");
                break;
            case ParameterSourceCandidate.Service:
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                    .Append(" = global::Manifold.Mcp.McpBinding.GetRequiredService<").Append(parameter.ParameterTypeName)
                    .AppendLine(">(services);");
                break;
            case ParameterSourceCandidate.Argument:
            case ParameterSourceCandidate.Option:
                if (parameter.Required)
                {
                    builder.Append("        if (!__uops_internal_mcpHasArgumentObject || !__uops_internal_mcpArgumentObject.TryGetProperty(")
                        .Append(GetUtf8Literal(displayName)).Append(", out global::System.Text.Json.JsonElement ").Append(boundName).AppendLine("Element))");
                    builder.Append("            throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"Missing required MCP argument '{displayName}'.")).AppendLine(");");
                    builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName).Append(" = ")
                        .Append(GetFastMcpParseExpression(parameter, boundName + "Element", displayName)).AppendLine(";");
                }
                else
                {
                    builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                        .Append(" = ").Append(GetFastCliDefaultExpression(parameter.ParameterTypeName)).AppendLine(";");
                    builder.Append("        if (__uops_internal_mcpHasArgumentObject && __uops_internal_mcpArgumentObject.TryGetProperty(").Append(GetUtf8Literal(displayName))
                        .Append(", out global::System.Text.Json.JsonElement ").Append(boundName).AppendLine("Element))");
                    builder.Append("            ").Append(boundName).Append(" = ")
                        .Append(GetFastMcpParseExpression(parameter, boundName + "Element", displayName)).AppendLine(";");
                }

                break;
            default:
                throw new InvalidOperationException("Unsupported MCP parameter source.");
        }
    }

    private static void AppendMcpToolDescriptor(StringBuilder builder, OperationCandidate operation)
    {
        builder.AppendLine("        new global::Manifold.Mcp.McpToolDescriptor(");
        builder.Append("            ").Append(ToLiteral(operation.McpToolName!)).AppendLine(",");
        builder.Append("            ").Append(ToLiteralOrNull(operation.Description)).AppendLine(",");
        builder.AppendLine("            [");
        foreach (ParameterCandidate parameter in operation.Parameters.Where(static candidate =>
                     candidate.Source is ParameterSourceCandidate.Argument or ParameterSourceCandidate.Option))
        {
            builder.AppendLine("                new global::Manifold.Mcp.McpParameterDescriptor(");
            builder.Append("                    ").Append(ToLiteral(parameter.McpName ?? parameter.Name)).AppendLine(",");
            builder.Append("                    typeof(").Append(parameter.ParameterTypeName).AppendLine("),");
            builder.Append("                    ").Append(parameter.Required ? "true" : "false").AppendLine(",");
            builder.Append("                    ").Append(ToLiteralOrNull(parameter.Description)).AppendLine("),");
        }

        builder.AppendLine("            ]),");
    }

    private static void AppendRequestBinding(
        StringBuilder builder,
        OperationCandidate operation,
        string requestVariableName,
        Func<ParameterCandidate, string> valueSelector)
    {
        if (string.IsNullOrWhiteSpace(operation.RequestTypeName))
            throw new InvalidOperationException("Request binding is only valid for instance operations.");
        if (valueSelector is null)
            throw new ArgumentNullException(nameof(valueSelector));

        IReadOnlyList<ParameterCandidate> requestParameters = operation.Parameters
            .Where(static parameter => parameter.Source is ParameterSourceCandidate.Option or ParameterSourceCandidate.Argument)
            .ToArray();

        builder.Append("        ").Append(operation.RequestTypeName).Append(' ').Append(requestVariableName).Append(" = new ")
            .Append(operation.RequestTypeName);
        if (requestParameters.Count == 0)
        {
            builder.AppendLine("();");
            return;
        }

        builder.AppendLine();
        builder.AppendLine("        {");
        foreach (ParameterCandidate parameter in requestParameters)
        {
            builder.Append("            ")
                .Append(parameter.RequestPropertyName ?? parameter.Name)
                .Append(" = ")
                .Append(valueSelector(parameter))
                .AppendLine(",");
        }

        builder.AppendLine("        };");
    }

    private static string GetGeneratedMcpReturnType(OperationCandidate operation)
    {
        return operation.ReturnKind switch
        {
            MethodReturnKind.Void => "global::System.Threading.Tasks.Task",
            MethodReturnKind.Value => "global::System.Threading.Tasks.Task<" + operation.ResultTypeName + ">",
            MethodReturnKind.Task => "global::System.Threading.Tasks.Task",
            MethodReturnKind.TaskOfT => operation.MethodReturnTypeName,
            MethodReturnKind.ValueTask => "global::System.Threading.Tasks.Task",
            MethodReturnKind.ValueTaskOfT => "global::System.Threading.Tasks.Task<" + operation.ResultTypeName + ">",
            _ => throw new InvalidOperationException("Unsupported MCP return kind.")
        };
    }

    private static void AppendMcpParameterList(StringBuilder builder, OperationCandidate operation)
    {
        bool first = true;
        foreach (ParameterCandidate parameter in operation.Parameters.Where(static parameter =>
                     parameter.Source is ParameterSourceCandidate.Option or ParameterSourceCandidate.Argument))
        {
            if (!first)
                builder.Append(", ");

            string mcpParameterName = parameter.McpName ?? parameter.Name;
            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                builder.Append("[global::System.ComponentModel.Description(")
                    .Append(ToLiteral(parameter.Description!))
                    .Append(")] ");
            }

            string identifier = GetMcpParameterName(parameter);
            if (!string.Equals(identifier, mcpParameterName, StringComparison.Ordinal))
            {
                builder.Append("[global::System.Text.Json.Serialization.JsonPropertyName(")
                    .Append(ToLiteral(mcpParameterName))
                    .Append(")] ");
            }

            builder.Append(parameter.ParameterTypeName)
                .Append(' ')
                .Append(identifier);
            first = false;
        }

        ParameterCandidate? cancellationTokenParameter = operation.Parameters.FirstOrDefault(static parameter =>
            parameter.Source == ParameterSourceCandidate.CancellationToken);
        if (cancellationTokenParameter is not null)
        {
            if (!first)
                builder.Append(", ");

            builder.Append("global::System.Threading.CancellationToken ")
                .Append(GetMcpParameterName(cancellationTokenParameter))
                .Append(" = default");
        }
    }

    private static void AppendCliResultReturn(StringBuilder builder, OperationCandidate operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.FormatterTypeName))
        {
            builder.Append("        ").Append(operation.FormatterTypeName).Append(" formatter = global::Manifold.Cli.CliBinding.GetRequiredServiceOrThrow<")
                .Append(operation.FormatterTypeName).AppendLine(">(services);");
            builder.Append("        string? text = formatter.FormatText(result, global::Manifold.OperationContext.ForCli(")
                .Append(ToLiteral(operation.OperationId))
                .AppendLine(", services, cancellationToken: cancellationToken));");
        }
        else
        {
            builder.AppendLine("        string? text = global::Manifold.Cli.CliBinding.FormatDefaultText(result);");
        }

        builder.Append("        return ");
        if (operation.ReturnKind is MethodReturnKind.Value)
            builder.Append("global::System.Threading.Tasks.ValueTask.FromResult(");
        builder.Append("new global::Manifold.Cli.CliInvocationResult(result, typeof(")
            .Append(operation.ResultTypeName)
            .Append("), text)");
        if (operation.ReturnKind is MethodReturnKind.Value)
            builder.Append(')');
        builder.AppendLine(";");
    }

    private static void AppendFastCliResultReturn(StringBuilder builder, OperationCandidate operation, bool wrapInValueTask)
    {
        if (!string.IsNullOrWhiteSpace(operation.FormatterTypeName))
        {
            builder.Append("        ").Append(operation.FormatterTypeName).Append(" formatter = global::Manifold.Cli.CliBinding.GetRequiredServiceOrThrow<")
                .Append(operation.FormatterTypeName).AppendLine(">(services);");
            AppendFastCliResultExpressionPrefix(builder, wrapInValueTask);
            builder.Append("global::Manifold.Cli.FastCliInvocationResult.FromText(formatter.FormatText(result, global::Manifold.OperationContext.ForCli(")
                .Append(ToLiteral(operation.OperationId))
                .Append(", services, cancellationToken: cancellationToken)))");
            AppendFastCliResultExpressionSuffix(builder, wrapInValueTask);
            return;
        }

        if (TryGetFastCliResultFactory(operation.ResultTypeName, out string? factoryMethodName))
        {
            AppendFastCliResultExpressionPrefix(builder, wrapInValueTask);
            builder.Append("global::Manifold.Cli.FastCliInvocationResult.")
                .Append(factoryMethodName)
                .Append("(result)");
            AppendFastCliResultExpressionSuffix(builder, wrapInValueTask);
            return;
        }

        AppendFastCliResultExpressionPrefix(builder, wrapInValueTask);
        builder.Append("global::Manifold.Cli.FastCliInvocationResult.FromText(global::Manifold.Cli.CliBinding.FormatDefaultText(result))");
        AppendFastCliResultExpressionSuffix(builder, wrapInValueTask);
    }

    private static void AppendFastCliResultExpressionPrefix(StringBuilder builder, bool wrapInValueTask)
    {
        builder.Append("        return ");
        if (wrapInValueTask)
            builder.Append("global::System.Threading.Tasks.ValueTask.FromResult(");
    }

    private static void AppendFastCliResultExpressionSuffix(StringBuilder builder, bool wrapInValueTask)
    {
        if (wrapInValueTask)
            builder.Append(')');

        builder.AppendLine(";");
    }

    private static bool TryGetFastCliResultFactory(string resultTypeName, out string? factoryMethodName)
    {
        factoryMethodName = resultTypeName switch
        {
            "string" or "global::System.String" => "FromText",
            "bool" or "global::System.Boolean" => "FromBoolean",
            "int" or "global::System.Int32" => "FromNumber",
            "long" or "global::System.Int64" => "FromLargeNumber",
            "double" or "global::System.Double" => "FromRealNumber",
            "decimal" or "global::System.Decimal" => "FromPreciseNumber",
            "global::System.Guid" => "FromIdentifier",
            "global::System.DateTimeOffset" => "FromTimestamp",
            _ => null
        };

        return factoryMethodName is not null;
    }

    private static void AppendOperationResultReturn(StringBuilder builder, OperationCandidate operation, bool wrapInValueTask)
    {
        if (!string.IsNullOrWhiteSpace(operation.FormatterTypeName))
        {
            builder.Append("        ").Append(operation.FormatterTypeName).Append(" formatter = global::Manifold.OperationBinding.GetRequiredService<")
                .Append(operation.FormatterTypeName).AppendLine(">(services);");
            builder.Append("        string? displayText = formatter.FormatText(result, new global::Manifold.OperationContext(")
                .Append(ToLiteral(operation.OperationId))
                .AppendLine(", surface, services, cancellationToken: cancellationToken));");
        }
        else
        {
            builder.AppendLine("        string? displayText = null;");
        }

        builder.Append("        return ");
        if (wrapInValueTask)
            builder.Append("global::System.Threading.Tasks.ValueTask.FromResult(");
        builder.Append("new global::Manifold.OperationInvocationResult(result, typeof(")
            .Append(operation.ResultTypeName)
            .Append("), displayText)");
        if (wrapInValueTask)
            builder.Append(')');
        builder.AppendLine(";");
    }

    private static void AppendFastMcpResultReturn(StringBuilder builder, OperationCandidate operation, bool wrapInValueTask)
    {
        if (TryGetFastMcpResultFactory(operation.ResultTypeName, out string? factoryMethodName))
        {
            AppendFastMcpResultExpressionPrefix(builder, wrapInValueTask);
            builder.Append("global::Manifold.Mcp.FastMcpInvocationResult.")
                .Append(factoryMethodName)
                .Append("(result)");
            AppendFastMcpResultExpressionSuffix(builder, wrapInValueTask);
            return;
        }

        AppendFastMcpResultExpressionPrefix(builder, wrapInValueTask);
        builder.Append("global::Manifold.Mcp.FastMcpInvocationResult.FromStructured(result, typeof(")
            .Append(operation.ResultTypeName)
            .Append("))");
        AppendFastMcpResultExpressionSuffix(builder, wrapInValueTask);
    }

    private static void AppendFastMcpResultExpressionPrefix(StringBuilder builder, bool wrapInValueTask)
    {
        builder.Append("        return ");
        if (wrapInValueTask)
            builder.Append("global::System.Threading.Tasks.ValueTask.FromResult(");
    }

    private static void AppendFastMcpResultExpressionSuffix(StringBuilder builder, bool wrapInValueTask)
    {
        if (wrapInValueTask)
            builder.Append(')');

        builder.AppendLine(";");
    }

    private static bool TryGetFastMcpResultFactory(string resultTypeName, out string? factoryMethodName)
    {
        factoryMethodName = resultTypeName switch
        {
            "string" or "global::System.String" => "FromText",
            "bool" or "global::System.Boolean" => "FromBoolean",
            "int" or "global::System.Int32" => "FromNumber",
            "long" or "global::System.Int64" => "FromLargeNumber",
            "double" or "global::System.Double" => "FromRealNumber",
            "decimal" or "global::System.Decimal" => "FromPreciseNumber",
            "global::System.Guid" => "FromIdentifier",
            "global::System.DateTimeOffset" => "FromTimestamp",
            _ => null
        };

        return factoryMethodName is not null;
    }

    private static void AppendCliParameterBinding(StringBuilder builder, ParameterCandidate parameter)
    {
        string boundName = GetBoundVariableName(parameter);
        switch (parameter.Source)
        {
            case ParameterSourceCandidate.CancellationToken:
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                    .AppendLine(" = cancellationToken;");
                break;
            case ParameterSourceCandidate.Service:
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                    .Append(" = global::Manifold.Cli.CliBinding.GetRequiredService<").Append(parameter.ParameterTypeName)
                    .AppendLine(">(services);");
                break;
            case ParameterSourceCandidate.Argument:
                if (parameter.Required)
                {
                    string cliDisplayName = parameter.CliName ?? parameter.Name;
                    builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName)
                        .Append(" = (").Append(parameter.ParameterTypeName).Append(")global::Manifold.Cli.CliBinding.ConvertValue(typeof(")
                        .Append(parameter.ParameterTypeName).Append("), global::Manifold.Cli.CliBinding.GetRequiredArgument(arguments, ")
                        .Append(parameter.Position!.Value.ToString(CultureInfo.InvariantCulture)).Append(", ")
                        .Append(ToLiteral(cliDisplayName)).Append("), ").Append(ToLiteral(cliDisplayName)).AppendLine(")!;");
                }
                else
                {
                    string cliDisplayName = parameter.CliName ?? parameter.Name;
                    builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName).Append(" = arguments.Count > ")
                        .Append(parameter.Position!.Value.ToString(CultureInfo.InvariantCulture))
                        .Append(" ? (").Append(parameter.ParameterTypeName)
                        .Append(")global::Manifold.Cli.CliBinding.ConvertValue(typeof(").Append(parameter.ParameterTypeName)
                        .Append("), arguments[").Append(parameter.Position!.Value.ToString(CultureInfo.InvariantCulture)).Append("], ")
                        .Append(ToLiteral(cliDisplayName)).AppendLine(")! : default!;");
                }

                break;
            case ParameterSourceCandidate.Option:
                string cliOptionName = parameter.CliName ?? parameter.Name;
                builder.Append("        ").Append(parameter.ParameterTypeName).Append(' ').Append(boundName);
                if (parameter.Required)
                {
                    builder.Append(" = global::Manifold.Cli.CliBinding.TryFindOptionValue(options, ")
                        .Append(ToLiteral(cliOptionName)).Append(", ")
                        .Append(GetStringArrayExpression(parameter.Aliases))
                        .Append(", out string? ").Append(boundName).Append("Text) ? (")
                        .Append(parameter.ParameterTypeName)
                        .Append(")global::Manifold.Cli.CliBinding.ConvertValue(typeof(").Append(parameter.ParameterTypeName)
                        .Append("), ").Append(boundName).Append("Text!, ").Append(ToLiteral(cliOptionName))
                        .Append(")! : throw new global::System.ArgumentException(")
                        .Append(ToLiteral($"Missing required --{cliOptionName} option.")).AppendLine(");");
                }
                else
                {
                    builder.Append(" = global::Manifold.Cli.CliBinding.TryFindOptionValue(options, ")
                        .Append(ToLiteral(cliOptionName)).Append(", ")
                        .Append(GetStringArrayExpression(parameter.Aliases))
                        .Append(", out string? ").Append(boundName).Append("Text) ? (")
                        .Append(parameter.ParameterTypeName)
                        .Append(")global::Manifold.Cli.CliBinding.ConvertValue(typeof(").Append(parameter.ParameterTypeName)
                        .Append("), ").Append(boundName).Append("Text!, ").Append(ToLiteral(cliOptionName))
                        .AppendLine(")! : default;");
                }

                break;
            default:
                throw new InvalidOperationException("Unsupported CLI parameter source.");
        }
    }

    private static bool CanUseFastCliInvoker(OperationCandidate operation)
    {
        return operation.Parameters.All(IsFastCliParameterSupported);
    }

    private static bool CanUseFastSyncCliInvoker(OperationCandidate operation)
    {
        return CanUseFastCliInvoker(operation) &&
               operation.ReturnKind is MethodReturnKind.Void or MethodReturnKind.Value;
    }

    private static bool IsFastCliParameterSupported(ParameterCandidate parameter)
    {
        return parameter.Source switch
        {
            ParameterSourceCandidate.Service => true,
            ParameterSourceCandidate.CancellationToken => true,
            ParameterSourceCandidate.Argument or ParameterSourceCandidate.Option => TryGetFastCliParserMethod(parameter.ParameterTypeName, out _),
            _ => false
        };
    }

    private static IEnumerable<string> GetCliOptionNames(ParameterCandidate parameter)
    {
        yield return parameter.CliName ?? parameter.Name;
        if (parameter.Aliases is not null && !parameter.Aliases.Value.IsDefaultOrEmpty)
        {
            foreach (string alias in parameter.Aliases.Value)
                yield return alias;
        }
    }

    private static string GetFastCliDefaultExpression(string typeName)
    {
        return IsFastCliValueType(typeName) ? "default" : "default!";
    }

    private static bool IsFastCliValueType(string typeName)
    {
        return typeName.StartsWith("global::System.Nullable<", StringComparison.Ordinal) ||
               string.Equals(typeName, "bool", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.Boolean", StringComparison.Ordinal) ||
               string.Equals(typeName, "int", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.Int32", StringComparison.Ordinal) ||
               string.Equals(typeName, "long", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.Int64", StringComparison.Ordinal) ||
               string.Equals(typeName, "double", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.Double", StringComparison.Ordinal) ||
               string.Equals(typeName, "decimal", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.Decimal", StringComparison.Ordinal) ||
               string.Equals(typeName, "Guid", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.Guid", StringComparison.Ordinal) ||
               string.Equals(typeName, "DateTimeOffset", StringComparison.Ordinal) ||
               string.Equals(typeName, "global::System.DateTimeOffset", StringComparison.Ordinal);
    }

    private static string GetFastCliParseExpression(ParameterCandidate parameter, string valueExpression, string displayName)
    {
        if (!TryGetFastCliParserMethod(parameter.ParameterTypeName, out string? parserMethod))
            throw new InvalidOperationException($"The fast CLI invoker does not support parameter type '{parameter.ParameterTypeName}'.");

        return string.Equals(parserMethod, "identity", StringComparison.Ordinal)
            ? valueExpression
            : "global::Manifold.Cli.CliBinding." + parserMethod + "(" + valueExpression + ", " + ToLiteral(displayName) + ")";
    }

    private static string GetFastMcpParseExpression(ParameterCandidate parameter, string valueExpression, string displayName)
    {
        if (TryGetFastCliParserMethod(parameter.ParameterTypeName, out string? parserMethod))
        {
            return string.Equals(parserMethod, "identity", StringComparison.Ordinal)
                ? "global::Manifold.Mcp.McpBinding.ParseString(" + valueExpression + ", " + ToLiteral(displayName) + ")"
                : "global::Manifold.Mcp.McpBinding." + parserMethod + "(" + valueExpression + ", " + ToLiteral(displayName) + ")";
        }

        return "(" + parameter.ParameterTypeName + ")global::Manifold.Mcp.McpBinding.ConvertValue(typeof(" +
               parameter.ParameterTypeName + "), " + valueExpression + ", " + ToLiteral(displayName) + ")!";
    }

    private static string GetUtf8Literal(string text)
    {
        return ToLiteral(text) + "u8";
    }

    private static bool TryGetFastCliParserMethod(string typeName, out string? parserMethod)
    {
        string normalizedTypeName = typeName;
        if (normalizedTypeName.StartsWith("global::System.Nullable<", StringComparison.Ordinal) &&
            normalizedTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            normalizedTypeName = normalizedTypeName.Substring(
                "global::System.Nullable<".Length,
                normalizedTypeName.Length - "global::System.Nullable<".Length - 1);
        }

        parserMethod = normalizedTypeName switch
        {
            "string" => "identity",
            "global::System.String" => "identity",
            "bool" => "ParseBoolean",
            "global::System.Boolean" => "ParseBoolean",
            "int" => "ParseInt32",
            "global::System.Int32" => "ParseInt32",
            "long" => "ParseInt64",
            "global::System.Int64" => "ParseInt64",
            "double" => "ParseDouble",
            "global::System.Double" => "ParseDouble",
            "decimal" => "ParseDecimal",
            "global::System.Decimal" => "ParseDecimal",
            "Guid" => "ParseGuid",
            "global::System.Guid" => "ParseGuid",
            "Uri" => "ParseUri",
            "global::System.Uri" => "ParseUri",
            "DateTimeOffset" => "ParseDateTimeOffset",
            "global::System.DateTimeOffset" => "ParseDateTimeOffset",
            _ => null
        };

        return parserMethod is not null;
    }

    private static bool CanUseFastSyncMcpInvoker(OperationCandidate operation)
    {
        return operation.ReturnKind is MethodReturnKind.Void or MethodReturnKind.Value;
    }

    private static string GetOperationMethodBaseName(string operationId)
    {
        StringBuilder builder = new();
        bool upperNext = true;
        foreach (char character in operationId)
        {
            if (!char.IsLetterOrDigit(character))
            {
                upperNext = true;
                continue;
            }

            builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }

        return builder.ToString();
    }

    private static string GetBoundVariableName(ParameterCandidate parameter)
    {
        StringBuilder builder = new("__uops_");
        foreach (char character in parameter.Name)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');

        return builder.ToString();
    }

    private static string GetInvocationArgumentName(ParameterCandidate parameter)
    {
        return parameter.Source == ParameterSourceCandidate.Service
            ? GetBoundVariableName(parameter)
            : GetMcpParameterName(parameter);
    }

    private static string GetMcpParameterName(ParameterCandidate parameter)
    {
        string? candidate = string.IsNullOrWhiteSpace(parameter.McpName) ? parameter.Name : parameter.McpName;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = "value";
        else
            candidate = candidate!.Trim();
        string normalizedCandidate = candidate;
        StringBuilder builder = new();
        foreach (char character in normalizedCandidate)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');

        if (builder.Length == 0)
            builder.Append("value");

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
            builder.Insert(0, '_');

        return builder.ToString();
    }

    private static void AppendOperation(StringBuilder builder, OperationCandidate operation)
    {
        builder.AppendLine("        new global::Manifold.OperationDescriptor(");
        builder.Append("            ").Append(ToLiteral(operation.OperationId)).AppendLine(",");
        builder.Append("            typeof(").Append(operation.DeclaringTypeName).AppendLine("),");
        builder.Append("            ").Append(ToLiteral(operation.MethodName)).AppendLine(",");
        builder.Append("            typeof(").Append(operation.ResultTypeName).AppendLine("),");
        builder.Append("            global::Manifold.OperationVisibility.").Append(operation.Visibility).AppendLine(",");
        builder.AppendLine("            new global::Manifold.ParameterDescriptor[]");
        builder.AppendLine("            {");
        foreach (ParameterCandidate parameter in operation.Parameters)
            AppendParameter(builder, parameter);

        builder.AppendLine("            },");
        builder.Append("            ").Append(ToLiteralOrNull(operation.Description)).AppendLine(",");
        builder.Append("            ").Append(ToLiteralOrNull(operation.Summary)).AppendLine(",");
        builder.Append("            ").Append(GetStringArrayExpression(operation.CliCommandPath)).AppendLine(",");
        builder.Append("            ").Append(GetNestedStringArrayExpression(operation.CliCommandAliases)).AppendLine(",");
        builder.Append("            ").Append(ToLiteralOrNull(operation.McpToolName)).AppendLine(",");
        builder.Append("            ").Append(operation.Hidden ? "true" : "false").AppendLine(",");
        builder.Append("            ").Append(string.IsNullOrWhiteSpace(operation.RequestTypeName) ? "null" : "typeof(" + operation.RequestTypeName + ")").AppendLine("),");
    }

    private static void AppendParameter(StringBuilder builder, ParameterCandidate parameter)
    {
        builder.AppendLine("                new global::Manifold.ParameterDescriptor(");
        builder.Append("                    ").Append(ToLiteral(parameter.Name)).AppendLine(",");
        builder.Append("                    typeof(").Append(parameter.ParameterTypeName).AppendLine("),");
        builder.Append("                    global::Manifold.ParameterSource.").Append(parameter.Source).AppendLine(",");
        builder.Append("                    ").Append(parameter.Required ? "true" : "false").AppendLine(",");
        builder.Append("                    ").Append(parameter.Position?.ToString(CultureInfo.InvariantCulture) ?? "null").AppendLine(",");
        builder.Append("                    ").Append(ToLiteralOrNull(parameter.Description)).AppendLine(",");
        builder.Append("                    ").Append(GetStringArrayExpression(parameter.Aliases)).AppendLine(",");
        builder.Append("                    ").Append(ToLiteralOrNull(parameter.CliName)).AppendLine(",");
        builder.Append("                    ").Append(ToLiteralOrNull(parameter.McpName)).AppendLine(",");
        builder.Append("                    ").Append(ToLiteralOrNull(parameter.RequestPropertyName)).AppendLine("),");
    }

    private static string GetStringArrayExpression(ImmutableArray<string>? values)
    {
        return values is null || values.Value.IsDefaultOrEmpty
            ? "null"
            : "new string[] { " + string.Join(", ", values.Value.Select(ToLiteral)) + " }";
    }

    private static IEnumerable<ImmutableArray<string>> GetCliCommandPaths(OperationCandidate operation)
    {
        if (operation.CliCommandPath is { } cliCommandPath && !cliCommandPath.IsDefaultOrEmpty)
            yield return cliCommandPath;

        if (operation.CliCommandAliases is not { } cliCommandAliases || cliCommandAliases.IsDefaultOrEmpty)
            yield break;

        foreach (ImmutableArray<string> aliasPath in cliCommandAliases)
        {
            if (!aliasPath.IsDefaultOrEmpty)
                yield return aliasPath;
        }
    }

    private static string GetNestedStringArrayExpression(ImmutableArray<ImmutableArray<string>>? values)
    {
        return values is null || values.Value.IsDefaultOrEmpty
            ? "null"
            : "new global::System.Collections.Generic.IReadOnlyList<string>[] { " +
              string.Join(", ", values.Value.Select(static value => "new string[] { " + string.Join(", ", value.Select(ToLiteral)) + " }")) +
              " }";
    }

    private static string ToLiteral(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + "\"";
    }

    private static string ToLiteralOrNull(string? value)
    {
        return value is null ? "null" : ToLiteral(value);
    }

    private sealed class OperationCandidate
    {
        public OperationCandidate(
            string operationId,
            string declaringTypeName,
            string methodName,
            string methodReturnTypeName,
            string resultTypeName,
            MethodReturnKind returnKind,
            OperationVisibilityCandidate visibility,
            ImmutableArray<ParameterCandidate> parameters,
            string? description,
            string? summary,
            ImmutableArray<string>? cliCommandPath,
            ImmutableArray<ImmutableArray<string>>? cliCommandAliases,
            string? mcpToolName,
            string? formatterTypeName,
            bool hidden,
            InvocationKind invocationKind,
            string? requestTypeName)
        {
            OperationId = operationId;
            DeclaringTypeName = declaringTypeName;
            MethodName = methodName;
            MethodReturnTypeName = methodReturnTypeName;
            ResultTypeName = resultTypeName;
            ReturnKind = returnKind;
            Visibility = visibility;
            Parameters = parameters;
            Description = description;
            Summary = summary;
            CliCommandPath = cliCommandPath;
            CliCommandAliases = cliCommandAliases;
            McpToolName = mcpToolName;
            FormatterTypeName = formatterTypeName;
            Hidden = hidden;
            InvocationKind = invocationKind;
            RequestTypeName = requestTypeName;
        }

        public string OperationId { get; }
        public string DeclaringTypeName { get; }
        public string MethodName { get; }
        public string MethodReturnTypeName { get; }
        public string ResultTypeName { get; }
        public MethodReturnKind ReturnKind { get; }
        public OperationVisibilityCandidate Visibility { get; }
        public ImmutableArray<ParameterCandidate> Parameters { get; }
        public string? Description { get; }
        public string? Summary { get; }
        public ImmutableArray<string>? CliCommandPath { get; }
        public ImmutableArray<ImmutableArray<string>>? CliCommandAliases { get; }
        public string? McpToolName { get; }
        public string? FormatterTypeName { get; }
        public bool Hidden { get; }
        public InvocationKind InvocationKind { get; }
        public string? RequestTypeName { get; }
    }

    private sealed class OperationAnalysisResult
    {
        public OperationAnalysisResult(
            OperationCandidate? candidate,
            ImmutableArray<OperationDiagnostic> diagnostics)
        {
            Candidate = candidate;
            Diagnostics = diagnostics;
        }

        public OperationCandidate? Candidate { get; }
        public ImmutableArray<OperationDiagnostic> Diagnostics { get; }
    }

    private sealed class ParameterCandidate
    {
        public ParameterCandidate(
            string name,
            string parameterTypeName,
            ParameterSourceCandidate source,
            bool required,
            int? position,
            string? description,
            ImmutableArray<string>? aliases,
            string? cliName,
            string? mcpName,
            string? requestPropertyName = null)
        {
            Name = name;
            ParameterTypeName = parameterTypeName;
            Source = source;
            Required = required;
            Position = position;
            Description = description;
            Aliases = aliases;
            CliName = cliName;
            McpName = mcpName;
            RequestPropertyName = requestPropertyName;
        }

        public string Name { get; }
        public string ParameterTypeName { get; }
        public ParameterSourceCandidate Source { get; }
        public bool Required { get; }
        public int? Position { get; }
        public string? Description { get; }
        public ImmutableArray<string>? Aliases { get; }
        public string? CliName { get; }
        public string? McpName { get; }
        public string? RequestPropertyName { get; }
    }

    private sealed class ParameterAnalysisResult
    {
        private ParameterAnalysisResult(
            ParameterCandidate? candidate,
            ImmutableArray<OperationDiagnostic> diagnostics)
        {
            Candidate = candidate;
            Diagnostics = diagnostics;
        }

        public ParameterCandidate? Candidate { get; }
        public ImmutableArray<OperationDiagnostic> Diagnostics { get; }

        public static ParameterAnalysisResult FromCandidate(ParameterCandidate candidate)
        {
            return new ParameterAnalysisResult(candidate, []);
        }

        public static ParameterAnalysisResult FromDiagnostic(OperationDiagnostic diagnostic)
        {
            return new ParameterAnalysisResult(null, [diagnostic]);
        }
    }

    private sealed class OperationDiagnostic
    {
        public OperationDiagnostic(
            DiagnosticDescriptor descriptor,
            Location? location,
            object?[] messageArgs)
        {
            Descriptor = descriptor;
            Location = location;
            MessageArgs = messageArgs;
        }

        public DiagnosticDescriptor Descriptor { get; }
        public Location? Location { get; }
        public object?[] MessageArgs { get; }
    }

    private enum OperationVisibilityCandidate
    {
        Both = 0,
        CliOnly = 1,
        McpOnly = 2
    }

    private enum ParameterSourceCandidate
    {
        Option = 0,
        Argument = 1,
        Service = 2,
        CancellationToken = 3
    }

    private enum MethodReturnKind
    {
        Void = 0,
        Value = 1,
        Task = 2,
        TaskOfT = 3,
        ValueTask = 4,
        ValueTaskOfT = 5
    }

    private enum InvocationKind
    {
        StaticMethod = 0,
        InstanceOperation = 1
    }
}

internal static class OperationDescriptorGeneratorSymbolExtensions
{
    public static bool IsGenericTaskLike(this INamedTypeSymbol symbol, string metadataName)
    {
        return symbol.TypeArguments.Length == 1 &&
               string.Equals(symbol.OriginalDefinition.ToDisplayString(), metadataName + "<TResult>", StringComparison.Ordinal);
    }

    public static bool IsNonGenericTaskLike(this INamedTypeSymbol symbol, string metadataName)
    {
        return symbol.TypeArguments.Length == 0 &&
               string.Equals(symbol.ToDisplayString(), metadataName, StringComparison.Ordinal);
    }
}



