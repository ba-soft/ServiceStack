using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ServiceStack.Configuration;
using ServiceStack.IO;
using ServiceStack.Script;
using ServiceStack.Text;

namespace ServiceStack.Script
{
    public class ScriptContext : IDisposable
    {
        public List<PageFormat> PageFormats { get; set; } = new List<PageFormat>();
        
        public string IndexPage { get; set; } = "index";

        public string DefaultLayoutPage { get; set; } = "_layout";

        public ISharpPages Pages { get; set; }

        public IVirtualPathProvider VirtualFiles { get; set; } = new MemoryVirtualFiles();
        
        public Dictionary<string, object> Args { get; } = new Dictionary<string, object>();

        public bool DebugMode { get; set; } = true;

        public PageFormat GetFormat(string extension) => PageFormats.FirstOrDefault(x => x.Extension == extension);

        public List<Type> ScanTypes { get; set; } = new List<Type>();

        public List<Assembly> ScanAssemblies { get; set; } = new List<Assembly>();

        public IContainer Container { get; set; } = new SimpleContainer();
        
        public IAppSettings AppSettings { get; set; } = new SimpleAppSettings();

        public List<ScriptMethods> ScriptMethods { get; } = new List<ScriptMethods>();

        public List<ScriptBlock> ScriptBlocks { get; } = new List<ScriptBlock>();

        public Dictionary<string, Type> CodePages { get; } = new Dictionary<string, Type>();
        
        public HashSet<string> ExcludeFiltersNamed { get; } = new HashSet<string>();

        private readonly Dictionary<string, ScriptBlock> blocksMap = new Dictionary<string, ScriptBlock>(); 
        public ScriptBlock GetBlock(string name) => blocksMap.TryGetValue(name, out var block) ? block : null;

        public ConcurrentDictionary<string, object> Cache { get; } = new ConcurrentDictionary<string, object>();

        public ConcurrentDictionary<ReadOnlyMemory<char>, object> CacheMemory { get; } = new ConcurrentDictionary<ReadOnlyMemory<char>, object>();

        public ConcurrentDictionary<string, Tuple<DateTime, object>> ExpiringCache { get; } = new ConcurrentDictionary<string, Tuple<DateTime, object>>();
        
        public ConcurrentDictionary<ReadOnlyMemory<char>, JsToken> JsTokenCache { get; } = new ConcurrentDictionary<ReadOnlyMemory<char>, JsToken>();

        public ConcurrentDictionary<string, Action<ScriptScopeContext, object, object>> AssignExpressionCache { get; } = new ConcurrentDictionary<string, Action<ScriptScopeContext, object, object>>();

        public ConcurrentDictionary<Type, Tuple<MethodInfo, MethodInvoker>> CodePageInvokers { get; } = new ConcurrentDictionary<Type, Tuple<MethodInfo, MethodInvoker>>();

        public ConcurrentDictionary<string, string> PathMappings { get; } = new ConcurrentDictionary<string, string>();
       
        public List<IScriptPlugin> Plugins { get; } = new List<IScriptPlugin>();
        
        public HashSet<string> FileFilterNames { get; } = new HashSet<string> { "includeFile", "fileContents" };
        
        /// <summary>
        /// Available transformers that can transform context filter stream outputs
        /// </summary>
        public Dictionary<string, Func<Stream, Task<Stream>>> FilterTransformers { get; set; } = new Dictionary<string, Func<Stream, Task<Stream>>>();

        /// <summary>
        /// Whether to check for modified pages by default when not in DebugMode
        /// </summary>
        public bool CheckForModifiedPages { get; set; } = false;

        /// <summary>
        /// How long in between checking for modified pages
        /// </summary>
        public TimeSpan? CheckForModifiedPagesAfter { get; set; }
        
        /// <summary>
        /// Existing caches and pages created prior to specified date should be invalidated 
        /// </summary>
        public DateTime? InvalidateCachesBefore { get; set; }
        
        /// <summary>
        /// Render render filter exceptions in-line where filter is located
        /// </summary>
        public bool RenderExpressionExceptions { get; set; }

        /// <summary>
        /// What argument to assign Filter Exceptions to
        /// </summary>
        public string AssignExceptionsTo { get; set; }
        
        /// <summary>
        /// Whether to skip executing Filters if an Exception was thrown
        /// </summary>
        public bool SkipExecutingFiltersIfError { get; set; }

        public Func<PageVariableFragment, ReadOnlyMemory<byte>> OnUnhandledExpression { get; set; } = DefaultOnUnhandledExpression;

        public SharpPage GetPage(string virtualPath)
        {
            var page = Pages.GetPage(virtualPath);
            if (page == null)
                throw new FileNotFoundException($"Page at path was not found: '{virtualPath}'");

            return page;
        }

        public DefaultScripts DefaultScripts => ScriptMethods.FirstOrDefault(x => x is DefaultScripts) as DefaultScripts;
        public ProtectedScripts ProtectedScripts => ScriptMethods.FirstOrDefault(x => x is ProtectedScripts) as ProtectedScripts;
        public HtmlScripts HtmlScripts => ScriptMethods.FirstOrDefault(x => x is HtmlScripts) as HtmlScripts;

        public void GetPage(string fromVirtualPath, string virtualPath, out SharpPage page, out SharpCodePage codePage)
        {
            if (!TryGetPage(fromVirtualPath, virtualPath, out page, out codePage))
                throw new FileNotFoundException($"Page at path was not found: '{virtualPath}'");            
        }
        
        public bool TryGetPage(string fromVirtualPath, string virtualPath, out SharpPage page, out SharpCodePage codePage)
        {
            var pathMapKey = nameof(GetPage) + ">" + fromVirtualPath;
            var mappedPath = GetPathMapping(pathMapKey, virtualPath);
            if (mappedPath != null)
            {
                var mappedPage = Pages.GetPage(mappedPath);
                if (mappedPage != null)
                {
                    page = mappedPage;
                    codePage = null;
                    return true;
                }
                RemovePathMapping(pathMapKey, mappedPath);
            }

            var tryExactMatch = virtualPath.IndexOf('/') >= 0; //if nested path specified, look for an exact match first
            if (tryExactMatch)
            {
                var cp = GetCodePage(virtualPath);
                if (cp != null)
                {
                    codePage = cp;
                    page = null;
                    return true;
                }

                var p = Pages.GetPage(virtualPath);
                if (p != null)
                {
                    page = p;
                    codePage = null;
                    return true;
                }
            }
            
            //otherwise find closest match from page.VirtualPath
            var parentPath = fromVirtualPath.IndexOf('/') >= 0
                ? fromVirtualPath.LastLeftPart('/')
                : "";
            do
            {
                var seekPath = parentPath.CombineWith(virtualPath);
                var cp = GetCodePage(seekPath);
                if (cp != null)
                {
                    codePage = cp;
                    page = null;
                    return true;
                }

                var p = Pages.GetPage(seekPath);
                if (p != null)
                {
                    page = p;
                    codePage = null;
                    SetPathMapping(pathMapKey, virtualPath, seekPath);
                    return true;
                }

                if (parentPath == "")
                    break;
                    
                parentPath = parentPath.IndexOf('/') >= 0
                    ? parentPath.LastLeftPart('/')
                    : "";

            } while (true);

            page = null;
            codePage = null;
            return false;
        }

        private SharpPage emptyPage;
        public SharpPage EmptyPage => emptyPage ?? (emptyPage = OneTimePage("")); 

        public SharpPage OneTimePage(string contents, string ext=null) 
            => Pages.OneTimePage(contents, ext ?? PageFormats.First().Extension);

        public SharpCodePage GetCodePage(string virtualPath)
        {
            var santizePath = virtualPath.Replace('\\','/').TrimPrefixes("/").LastLeftPart('.');

            var isIndexPage = santizePath == string.Empty || santizePath.EndsWith("/");
            var lookupPath = !isIndexPage
                ? santizePath
                : santizePath + IndexPage;
            
            if (!CodePages.TryGetValue(lookupPath, out Type type)) 
                return null;
            
            var instance = (SharpCodePage) Container.Resolve(type);
            instance.Init();
            return instance;
        }

        public string SetPathMapping(string prefix, string mapPath, string toPath) 
        {
            if (!DebugMode && toPath != null && mapPath != toPath)
                PathMappings[prefix + ">" + mapPath] = toPath;

            return toPath;
        }

        public void RemovePathMapping(string prefix, string mapPath) 
        {
            if (DebugMode)
                return;

            if (mapPath != null)
                PathMappings.TryRemove(prefix + ">" + mapPath, out _);
        }

        public string GetPathMapping(string prefix, string key)
        {
            if (DebugMode)
                return null;

            if (PathMappings.TryGetValue(prefix + ">" + key, out string mappedPath))
                return mappedPath;

            return null;
        }

        public ScriptContext()
        {
            Pages = new SharpPages(this);
            PageFormats.Add(new HtmlPageFormat());
            ScriptMethods.Add(new DefaultScripts());
            ScriptMethods.Add(new HtmlScripts());
            Plugins.Add(new DefaultScriptBlocks());
            Plugins.Add(new HtmlScriptBlocks());
            FilterTransformers[ScriptConstants.HtmlEncode] = HtmlPageFormat.HtmlEncodeTransformer;
            FilterTransformers["end"] = stream => (TypeConstants.EmptyByteArray.InMemoryStream() as Stream).InTask();
            FilterTransformers["buffer"] = stream => stream.InTask();
            
            Args[nameof(ScriptConfig.MaxQuota)] = ScriptConfig.MaxQuota;
            Args[nameof(ScriptConfig.DefaultCulture)] = ScriptConfig.CreateCulture();
            Args[nameof(ScriptConfig.DefaultDateFormat)] = ScriptConfig.DefaultDateFormat;
            Args[nameof(ScriptConfig.DefaultDateTimeFormat)] = ScriptConfig.DefaultDateTimeFormat;
            Args[nameof(ScriptConfig.DefaultTimeFormat)] = ScriptConfig.DefaultTimeFormat;
            Args[nameof(ScriptConfig.DefaultFileCacheExpiry)] = ScriptConfig.DefaultFileCacheExpiry;
            Args[nameof(ScriptConfig.DefaultUrlCacheExpiry)] = ScriptConfig.DefaultUrlCacheExpiry;
            Args[nameof(ScriptConfig.DefaultIndent)] = ScriptConfig.DefaultIndent;
            Args[nameof(ScriptConfig.DefaultNewLine)] = ScriptConfig.DefaultNewLine;
            Args[nameof(ScriptConfig.DefaultJsConfig)] = ScriptConfig.DefaultJsConfig;
            Args[nameof(ScriptConfig.DefaultStringComparison)] = ScriptConfig.DefaultStringComparison;
            Args[nameof(ScriptConfig.DefaultTableClassName)] = ScriptConfig.DefaultTableClassName;
            Args[nameof(ScriptConfig.DefaultErrorClassName)] = ScriptConfig.DefaultErrorClassName;
        }

        public ScriptContext RemoveFilters(Predicate<ScriptMethods> match)
        {
            ScriptMethods.RemoveAll(match);
            return this;
        }

        public ScriptContext RemoveBlocks(Predicate<ScriptBlock> match)
        {
            ScriptBlocks.RemoveAll(match);
            return this;
        }

        public ScriptContext RemovePlugins(Predicate<IScriptPlugin> match)
        {
            Plugins.RemoveAll(match);
            return this;
        }
        
        public Action<ScriptContext> OnAfterPlugins { get; set; }

        public bool HasInit { get; private set; }

        public ScriptContext Init()
        {
            if (HasInit)
                return this;
            HasInit = true;

            Args[ScriptConstants.Debug] = DebugMode;
            
            Container.AddSingleton(() => this);
            Container.AddSingleton(() => Pages);

            var beforePlugins = Plugins.OfType<IScriptPluginBefore>();
            foreach (var plugin in beforePlugins)
            {
                plugin.BeforePluginsLoaded(this);
            }
            foreach (var plugin in Plugins)
            {
                plugin.Register(this);
            }
            
            OnAfterPlugins?.Invoke(this);

            foreach (var type in ScanTypes)
            {
                ScanType(type);
            }

            foreach (var assembly in ScanAssemblies.Safe())
            {
                foreach (var type in assembly.GetTypes())
                {
                    ScanType(type);
                }
            }

            foreach (var method in ScriptMethods)
            {
                InitMethod(method);
            }

            foreach (var block in ScriptBlocks)
            {
                InitBlock(block);
                blocksMap[block.Name] = block;
            }

            var afterPlugins = Plugins.OfType<IScriptPluginAfter>();
            foreach (var plugin in afterPlugins)
            {
                plugin.AfterPluginsLoaded(this);
            }

            return this;
        }

        internal void InitMethod(ScriptMethods method)
        {
            if (method == null) return;
            if (method.Context == null)
                method.Context = this;
            if (method.Pages == null)
                method.Pages = Pages;
        }

        internal void InitBlock(ScriptBlock block)
        {
            if (block == null) return;
            if (block.Context == null)
                block.Context = this;
            if (block.Pages == null)
                block.Pages = Pages;
        }

        public ScriptContext ScanType(Type type)
        {
            if (!type.IsAbstract)
            {
                if (typeof(ScriptMethods).IsAssignableFrom(type))
                {
                    if (ScriptMethods.All(x => x?.GetType() != type))
                    {
                        Container.AddSingleton(type);
                        var filter = (ScriptMethods)Container.Resolve(type);
                        ScriptMethods.Add(filter);
                    }
                }
                else if (typeof(ScriptBlock).IsAssignableFrom(type))
                {
                    if (ScriptBlocks.All(x => x?.GetType() != type))
                    {
                        Container.AddSingleton(type);
                        var block = (ScriptBlock)Container.Resolve(type);
                        ScriptBlocks.Add(block);
                    }
                }
                else if (typeof(SharpCodePage).IsAssignableFrom(type))
                {
                    if (CodePages.Values.All(x => x != type))
                    {
                        Container.AddTransient(type);
                        var pageAttr = type.FirstAttribute<PageAttribute>();
                        if (pageAttr?.VirtualPath != null)
                        {
                            CodePages[pageAttr.VirtualPath] = type;
                        }
                    }
                }
            }
            
            return this;
        }

        public Action<ScriptScopeContext, object, object> GetAssignExpression(Type targetType, ReadOnlyMemory<char> expression)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));
            if (expression.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(expression));

            var key = targetType.FullName + ':' + expression;

            if (AssignExpressionCache.TryGetValue(key, out var fn))
                return fn;

            AssignExpressionCache[key] = fn = SharpPageUtils.CompileAssign(targetType, expression);

            return fn;
        }

        protected static ReadOnlyMemory<byte> DefaultOnUnhandledExpression(PageVariableFragment var) => 
            ScriptConfig.HideUnknownExpressions ? null : var.OriginalTextUtf8;

        public void Dispose()
        {
            using (Container as IDisposable) {}
        }
    }

    public static class ScriptContextExtensions
    {
        public static string EvaluateScript(this ScriptContext context, string template, Dictionary<string, object> args=null)
        {
            var pageResult = new PageResult(context.OneTimePage(template));
            args.Each((x,y) => pageResult.Args[x] = y);
            return pageResult.Result;
        }
        
        public static Task<string> EvaluateScriptAsync(this ScriptContext context, string template, Dictionary<string, object> args=null)
        {
            var pageResult = new PageResult(context.OneTimePage(template));
            args.Each((x,y) => pageResult.Args[x] = y);
            return pageResult.RenderToStringAsync();
        }
    }
}