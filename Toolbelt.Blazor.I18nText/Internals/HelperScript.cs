﻿using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Toolbelt.Blazor.I18nText.Internals
{
    internal class HelperScript : IAsyncDisposable
    {
        private delegate ValueTask<T> ScriptInvoker<T>(string identifier, params object[] args);

        private bool ScriptLoaded = false;

        private readonly SemaphoreSlim Syncer = new SemaphoreSlim(1, 1);

        private readonly IJSRuntime JSRuntime;

        private static readonly string Namespace = "Toolbelt.Blazor.I18nText";

        private bool? _IsOnline = null;

        public HelperScript(IJSRuntime jSRuntime)
        {
            this.JSRuntime = jSRuntime;
        }

        private string GetVersionText()
        {
            var assembly = this.GetType().Assembly;
            var version = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? assembly.GetName().Version!.ToString();
            return version;
        }

        private IJSObjectReference? JSModule = null;

        private async ValueTask<ScriptInvoker<T>?> EnsureScriptEnabledAsync<T>()
        {
            if (!this.ScriptLoaded)
            {
                await this.Syncer.WaitAsync();
                try
                {
                    if (!this.ScriptLoaded)
                    {
                        var scriptPath = "./_content/" + Namespace + "/script.module.min.js";//?v=" + version;

                        // Add version string for refresh token only navigator is online.
                        // (If the app runs on the offline mode, the module url with query parameters might cause the "resource not found" error.)
                        const string moduleScript = "export function isOnLine(){ return navigator.onLine; }";
                        await using var inlineJsModule = await this.JSRuntime.InvokeAsync<IJSObjectReference>("import", "data:text/javascript;charset=utf-8," + Uri.EscapeDataString(moduleScript));
                        var isOnLine = await inlineJsModule.InvokeAsync<bool>("isOnLine");
                        this._IsOnline = isOnLine;

                        if (isOnLine) scriptPath += $"?v={this.GetVersionText()}";

                        this.JSModule = await this.JSRuntime.InvokeAsync<IJSObjectReference>("import", scriptPath);
                        this.ScriptLoaded = true;
                    }
                }
                catch { }
                finally { this.Syncer.Release(); }
            }

            return this.JSModule == null ? default(ScriptInvoker<T>) : this.JSModule.InvokeAsync<T>;
        }

        internal static async ValueTask<string> DefaultGetInitialLanguageAsync(IServiceProvider serviceProvider, I18nTextOptions options)
        {
            var helperScript = serviceProvider.GetRequiredService<HelperScript>();
            var invoker = await helperScript.EnsureScriptEnabledAsync<string>();
            if (invoker == null) return "en-US";
            return await invoker.Invoke(Namespace + ".initLang", options.PersistanceLevel);
        }

        internal static async ValueTask DefaultPersistCurrentLanguageAsync(IServiceProvider serviceProvider, string langCode, I18nTextOptions options)
        {
            var helperScript = serviceProvider.GetRequiredService<HelperScript>();
            var invoker = await helperScript.EnsureScriptEnabledAsync<object>();
            if (invoker == null) return;
            await invoker.Invoke(Namespace + ".setCurrentLang", langCode, options.PersistanceLevel);
        }

        public async ValueTask<bool> IsOnlineAsync()
        {
            if (!this._IsOnline.HasValue)
            {
                await this.EnsureScriptEnabledAsync<object>();
            }
            return this._IsOnline!.Value;
        }

        public async ValueTask DisposeAsync()
        {
            if (this.JSModule != null) { await this.JSModule.DisposeAsync(); }
        }
    }
}
