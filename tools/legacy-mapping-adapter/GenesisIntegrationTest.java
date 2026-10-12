package dev.moonrise.mapping;
import java.nio.file.*;
import java.net.*;
import java.util.*;
import java.util.jar.*;
import com.moonsworth.lunar.ichor.*;
public class GenesisIntegrationTest {
 public static void main(String[] a) throws Exception {
  var stage=new OOIOHHHCIICRRRCICOIIIRRRICOCIC() {
   public String name(){return "offline";} public boolean hasMixinRuntime(){return false;}
   public boolean shouldUseParentAsMixinRuntime(){return false;} public boolean shouldUseClassBytes(){return false;}
  };
  var pipeline=new ROHIOOCICRORHRCIHHHHHHRICCCRRC(List.of(stage),null,GenesisIntegrationTest.class.getClassLoader());
  var loader=new com.moonsworth.lunar.genesis.ORCOIHCCICICIHRCCCCHHHRICHHICI(new URL[0],pipeline);
  byte[] bytes=LegacyMappingAdapter.convert(Path.of(a[0]));
  Path jar=Path.of(a[1]);
  try(var out=new JarOutputStream(Files.newOutputStream(jar))) {out.putNextEntry(new JarEntry(LegacyMappingAdapter.RESOURCE));out.write(bytes);out.closeEntry();}
  try { LegacyMappingAdapter.verifyResource(loader,jar,bytes); throw new AssertionError("Missing accepted"); }
  catch(IllegalStateException expected) { System.out.println("PASS missing rejected"); }
  loader.addURL(jar.toUri().toURL());
  System.out.println("RAW "+Collections.list(loader.getResources(LegacyMappingAdapter.RESOURCE)));
  LegacyMappingAdapter.verifyResource(loader,jar,bytes);
  System.out.println("PASS actual Genesis addURL/enumeration/getResourceAsStream");
  try { LegacyMappingAdapter.verifyResource(loader,jar,new byte[]{1}); throw new AssertionError("Bad content accepted"); }
  catch(IllegalStateException expected) { System.out.println("PASS wrong content rejected"); }
  Path duplicate=jar.resolveSibling("conflicting-resource.jar");
  Files.copy(jar,duplicate,StandardCopyOption.REPLACE_EXISTING);
  loader.addURL(duplicate.toUri().toURL());
  try { LegacyMappingAdapter.verifyResource(loader,jar,bytes); throw new AssertionError("Two origins accepted"); }
  catch(IllegalStateException expected) { System.out.println("PASS two distinct origins rejected"); }
  loader.close();

 }
}
