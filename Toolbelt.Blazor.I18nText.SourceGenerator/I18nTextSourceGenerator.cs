﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Toolbelt.Blazor.I18nText.Internals;
using Toolbelt.Blazor.I18nText.SourceGenerator.Inetrnals;

namespace Toolbelt.Blazor.I18nText.SourceGenerator
{
    [Generator]
    public class I18nTextSourceGenerator : ISourceGenerator
    {
        private readonly object _lock = new object();

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var options = this.CreateI18nTextCompilerOptions(context);
            if (!options.UseSourceGenerator) return;

            var cancellationToken = context.CancellationToken;

            var i18nTextSourceDirectory = options.I18nTextSourceDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var ignoreCase = StringComparison.InvariantCultureIgnoreCase;
            var srcFiles = context.AdditionalFiles
                .Where(item => item.Path.StartsWith(i18nTextSourceDirectory))
                .Where(item => item.Path.EndsWith(".json", ignoreCase) || item.Path.EndsWith(".csv", ignoreCase))
                .Select(item => new I18nTextSourceFile(item.Path, Encoding.UTF8 /*item.Encoding*/))
                .ToArray();

            //foreach (var src in srcFiles) Log.LogMessage($"- {src.Path}, {src.Encoding.BodyName}");

            var generatedSources = new ConcurrentBag<GeneratedSource>();
            var successOrNot = I18nTextCompiler.Compile(srcFiles, options, saveCode: (option, item, codeLines) =>
            {
                var hintName = Path.GetFileNameWithoutExtension(item.TypeFilePath) + ".g.cs";
                var sourceCode = string.Join("\n", codeLines);
                generatedSources.Add(new GeneratedSource(hintName, sourceCode));
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var source in generatedSources)
            {
                context.AddSource(source.HintName, source.SourceCode);
            }
        }

        private I18nTextCompilerOptions CreateI18nTextCompilerOptions(GeneratorExecutionContext context)
        {
            var globalOptions = context.AnalyzerConfigOptions.GlobalOptions;
            //if (!globalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace)) throw new Exception("Could not determin the root namespace.");
            if (!globalOptions.TryGetValue("build_property.I18nTextUseSourceGenerator", out var useSourceGenerator)) throw new Exception("Could not determin whether use source generator or not.");
            if (!globalOptions.TryGetValue("build_property.ProjectDir", out var projectDir)) throw new Exception("Could not determin the project diretcory.");
            if (!globalOptions.TryGetValue("build_property.I18nTextIntermediateDir", out var i18ntextIntermediateDir)) throw new Exception("Could not determin the i18ntext intermediate directory.");
            if (!globalOptions.TryGetValue("build_property.I18nTextNamespace", out var i18ntextNamespace)) throw new Exception("Could not determin the i18ntext namespace.");
            if (!globalOptions.TryGetValue("build_property.I18nTextSourceDirectory", out var i18nTextSourceDirectory)) throw new Exception("Could not determin the i18ntext source directory.");
            if (!globalOptions.TryGetValue("build_property.I18nTextFallBackLanguage", out var fallbackLang)) throw new Exception("Could not determin the fallback language.");
            if (!globalOptions.TryGetValue("build_property.I18nTextDisableSubNameSpace", out var disableSubNameSpace)) throw new Exception("Could not determin whether disable sub-namespace or not.");

            var options = new I18nTextCompilerOptions(baseDir: projectDir);
            options.UseSourceGenerator = bool.Parse(useSourceGenerator);
            options.I18nTextSourceDirectory = Path.Combine(projectDir, i18nTextSourceDirectory);
            options.OutDirectory = i18ntextIntermediateDir;
            options.NameSpace = i18ntextNamespace;
            options.DisableSubNameSpace = bool.Parse(disableSubNameSpace);
            options.FallBackLanguage = fallbackLang;

            options.LogMessage = this.CreateMessageReporter(context);
            options.LogError = this.CreateErrorReporter(context);

            return options;
        }

        private Action<string> CreateMessageReporter(GeneratorExecutionContext context)
        {
            return message =>
            {
                lock (this._lock)
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.Information, Location.None, message));
            };
        }

        private Action<Exception> CreateErrorReporter(GeneratorExecutionContext context)
        {
            return ex =>
            {
                var descriptor = (ex is I18nTextCompileException cex && DiagnosticDescriptors.TryGetByCode(cex.Code, out var d)) ? d : DiagnosticDescriptors.UnhandledException;
                lock (this._lock) context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, ex.Message));
            };
        }
    }
}
