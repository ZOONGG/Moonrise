package dev.moonrise.mapping;
import java.lang.instrument.*;
import java.lang.reflect.*;
import java.net.*;
import java.nio.file.*;
import java.util.*;
public class WeaveBootstrapIntegrationTest {
 static Instrumentation inst;
 public static void premain(String args, Instrumentation value) { inst=value; }
 public static void main(String[] a) throws Exception {
  try(var mods=Files.list(Path.of(System.getProperty("user.home"),".weave","mods"))) {
   if(mods.anyMatch(p->p.toString().endsWith(".jar"))) throw new IllegalStateException("Offline bootstrap requires zero mods");
  }
  MappingAgent.premain(String.join("|",a[0],a[1],a[4]),inst);
  URL genesis=Path.of(a[0]).toUri().toURL();
  var parent=new URLClassLoader("OfflineBootstrapGame",new URL[]{genesis},ClassLoader.getPlatformClassLoader()) {
   protected Class<?> loadClass(String name,boolean resolve) throws ClassNotFoundException {
    if(name.equals("net.weavemc.loader.bootstrap.URLClassLoaderAccessor")) return ClassLoader.getSystemClassLoader().loadClass(name);
    if((name.startsWith("org.spongepowered.") && !name.equals("org.spongepowered.asm.service.IMixinServiceBootstrap")) || name.startsWith("net.weavemc.") || name.startsWith("kotlin.") || name.startsWith("kotlinx.")) throw new ClassNotFoundException(name);
    return super.loadClass(name,resolve);
   }
  };
  var stageType=parent.loadClass("com.moonsworth.lunar.ichor.OOIOHHHCIICRRRCICOIIIRRRICOCIC");
  Object stage=java.lang.reflect.Proxy.newProxyInstance(parent,new Class[]{stageType},(p,m,args)->switch(m.getName()) {case "name", "toString" -> "offline"; case "hashCode" -> 1; case "equals" -> p==args[0]; default -> false;});
  var pipelineType=parent.loadClass("com.moonsworth.lunar.ichor.ROHIOOCICRORHRCIHHHHHHRICCCRRC");
  var configType=parent.loadClass("com.moonsworth.lunar.ichor.IHHHICHHRICRCIOHCIOHHOHIOIORRH");
  Path scratch=Path.of(a[4]).getParent();
  var config=configType.getConstructors()[0].newInstance(scratch,scratch,scratch,null,List.of(),List.of(),List.of(),Map.of());
  var pipeline=pipelineType.getConstructors()[0].newInstance(List.of(stage),config,parent);
  var loaderType=parent.loadClass("com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI");
  var loader=(URLClassLoader)loaderType.getConstructors()[0].newInstance(new URL[]{genesis,Path.of(a[0]).resolveSibling("forge-0.1.0-SNAPSHOT-all.jar").toUri().toURL(),Path.of(a[2]).toUri().toURL(),Path.of(a[3]).toUri().toURL()},pipeline);
  for(var url:loader.getURLs()) loaderType.getMethod("addURL",URL.class).invoke(loader,url);
  Thread.currentThread().setContextClassLoader(loader);
  var weave=loader.loadClass("net.weavemc.loader.WeaveLoader");
  System.out.println("DEFINING "+weave.getClassLoader().getClass().getName()+" name="+weave.getClassLoader().getName());
  try { weave.getMethod("init",Instrumentation.class).invoke(null,inst); }
  catch(InvocationTargetException e) {e.getCause().printStackTrace();System.exit(1);}
  var service=loader.loadClass("org.spongepowered.asm.service.MixinService").getMethod("getService").invoke(null);
  if(!service.getClass().getName().equals("net.weavemc.loader.mixins.WeaveMixinService") || service.getClass().getClassLoader()!=loader) throw new AssertionError("Wrong service identity");
  var mapper=loader.loadClass("net.weavemc.loader.mappings.XSrgMapper").getConstructor(String.class).newInstance("/"+LegacyMappingAdapter.RESOURCE);
  if(!mapper.getClass().getMethod("map",String.class).invoke(mapper,"ave").equals("net/minecraft/client/Minecraft")) throw new AssertionError("Actual XSrgMapper direction");
  var minecraftNode=service.getClass().getMethod("getClassNode",String.class).invoke(service,"net/minecraft/client/Minecraft");
  if(!minecraftNode.getClass().getField("name").get(minecraftNode).equals("net/minecraft/client/Minecraft")) throw new AssertionError("Bytecode provider mapping");
  var transformerType=loader.loadClass("net.weavemc.loader.mixins.WeaveMixinTransformer");
  Object transformer=transformerType.getField("INSTANCE").get(null);
  transformerType.getMethod("transform",ClassLoader.class,String.class,byte[].class).invoke(transformer,loader,"ave",Files.readAllBytes(Path.of(a[3],"ave.class")));
  System.out.println("PASS actual Weave bytecode provider remaps Minecraft fixture; post-init Mixin transformer accepts current environment");
  System.out.println("PASS actual Genesis defining loader; actual XSrgMapper; selected service; WeaveLoader.init returned; no Minecraft entrypoint invoked");
 }
}




