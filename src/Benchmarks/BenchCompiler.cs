using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using XamlIl.Ast;
using XamlIl.Parsers;
using XamlIl.Runtime;
using XamlIl.Transform;
using XamlIl.TypeSystem;

namespace Benchmarks
{
    public class ContentAttribute : Attribute
    {
        
    }
    
    public class BenchCompiler
    {
        static object s_asmLock = new object();
        public static Func<IServiceProvider, object> Compile(string xaml)
        {
            
            // Enforce everything to load
            foreach (var xt in typeof(BenchCompiler).Assembly.GetTypes())
            {
                xt.GetCustomAttributes();
                xt.GetInterfaces();
                foreach (var p in xt.GetProperties())
                    p.GetCustomAttributes();
            }
            typeof(IXamlIlParentStackProviderV1).Assembly.GetCustomAttributes();
            
            
            var typeSystem = new SreTypeSystem();
            var configuration = BenchmarksXamlIlConfiguration.Configure(typeSystem);
            var parsed = XDocumentXamlIlParser.Parse(xaml);
            
            var compiler = new XamlIlCompiler(configuration, true);
            compiler.Transform(parsed);
            
            
            var parsedTsType = ((IXamlIlAstValueNode) parsed.Root).Type.GetClrType();
            
#if !NETCOREAPP
            var path = Path.GetDirectoryName(typeof(BenchCompiler).Assembly.GetModules()[0].FullyQualifiedName);
            var da = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.RunAndSave,
                path);
#else
            var da = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
#endif
            
            var dm = da.DefineDynamicModule("testasm.dll");
            var t = dm.DefineType(Guid.NewGuid().ToString("N"), TypeAttributes.Public);

            var ctb = dm.DefineType(t.Name + "Context", TypeAttributes.Public);
            var  contextTypeDef = compiler.CreateContextType(((SreTypeSystem)typeSystem).CreateTypeBuilder(ctb));
            
            var parserTypeBuilder = ((SreTypeSystem) typeSystem).CreateTypeBuilder(t);
            compiler.Compile(parsed, parserTypeBuilder,  contextTypeDef, "Populate", "Build",
                "XamlIlNamespaceInfo", "https://github.com/kekekeks/XamlIl", null);
            
            var created = t.CreateType();

#if !NETCOREAPP
            dm.CreateGlobalFunctions();
            // Useful for debugging the actual MSIL, don't remove
            lock (s_asmLock)
                da.Save("testasm.dll");
#endif
            
            var isp = Expression.Parameter(typeof(IServiceProvider));
            return Expression.Lambda<Func<IServiceProvider, object>>(
                Expression.Convert(Expression.Call(
                    created.GetMethod("Build"), isp), typeof(object)), isp).Compile();

        }
    }
}