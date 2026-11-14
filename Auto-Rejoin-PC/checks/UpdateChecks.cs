using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using NexusRobloxManager;

static class UpdateChecks
{
    static int count;
    static void Assert(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
    static string Envelope(RSACryptoServiceProvider key, byte[] bytes, string channel, string url)
    {
        byte[] payload = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {
            channel=channel, version="9.0.0.0", url=url, sha256=UpdateService.Hash(bytes), size=bytes.Length, notes="Test fixture"
        }));
        return new JavaScriptSerializer().Serialize(new UpdateEnvelope { payload=Convert.ToBase64String(payload),
            signature=Convert.ToBase64String(key.SignData(payload, CryptoConfig.MapNameToOID("SHA256"))) });
    }
    static string Q(string text) { return "\""+text+"\""; }
    static void Compile(string arguments)
    {
        string compiler=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
        if(!File.Exists(compiler))compiler=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
        using(var process=Process.Start(new ProcessStartInfo(compiler,"/nologo /target:winexe "+arguments) {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true }))
        { string output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd(); process.WaitForExit(); if(process.ExitCode!=0)throw new Exception(output); }
    }
    public static int Run()
    {
        byte[] bytes=new byte[2048]; bytes[0]=77;bytes[1]=90;
        using(var key=new RSACryptoServiceProvider(2048))
        {
            key.PersistKeyInCsp=false;
            string xml=key.ToXmlString(false), json=Envelope(key,bytes,"Auto-Rejoin-PC","https://example.com/nexus.exe");
            var manifest=UpdateService.VerifyManifest(json,xml);
            Assert(manifest.channel=="Auto-Rejoin-PC" && manifest.version=="9.0.0.0", "signed PC update manifest verifies");
            var altered=new JavaScriptSerializer().Deserialize<UpdateEnvelope>(json);
            byte[] payload=Convert.FromBase64String(altered.payload);payload[payload.Length-2]^=1;altered.payload=Convert.ToBase64String(payload);
            Assert(Reject(delegate { UpdateService.VerifyManifest(new JavaScriptSerializer().Serialize(altered),xml); }),"modified update payload is rejected before parsing");
            using(var wrong=new RSACryptoServiceProvider(2048))
            { wrong.PersistKeyInCsp=false;Assert(Reject(delegate { UpdateService.VerifyManifest(json,wrong.ToXmlString(false)); }),"unknown release-signing key is rejected"); }
            Assert(Reject(delegate { UpdateService.VerifyManifest(Envelope(key,bytes,"Auto-Rejoin-Emu","https://example.com/a.exe"),xml); }),"Emulator updates cannot install into the PC channel");
            Assert(Reject(delegate { UpdateService.VerifyManifest(Envelope(key,bytes,"Auto-Rejoin-PC","http://example.com/a.exe"),xml); }),"non-HTTPS update download is rejected");
            UpdateService.VerifyBinary(bytes,manifest);Assert(true,"signed executable size and SHA-256 match");
            bytes[15]^=1;
            Assert(Reject(delegate { UpdateService.VerifyBinary(bytes,manifest); }),"corrupt downloaded executable is rejected");
            bytes[15]^=1;

            string directory=Path.Combine(WatchdogEngine.DataDir,"update-fixture-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string target=Path.Combine(directory,"installed.exe"), staged=Path.Combine(directory,"new.exe"), backup=target+".previous";
            byte[] old=Encoding.UTF8.GetBytes("old fixture");File.WriteAllBytes(target,old);File.WriteAllBytes(staged,bytes);
            Assert(Reject(delegate { UpdateService.ReplaceVerifiedFile(target,staged,new string('0',64),backup); }) && UpdateService.Hash(File.ReadAllBytes(target))==UpdateService.Hash(old),
                "changed installed executable blocks update without replacing it");
            UpdateService.ReplaceVerifiedFile(target,staged,UpdateService.Hash(old),backup);
            Assert(UpdateService.Hash(File.ReadAllBytes(target))==UpdateService.Hash(bytes) && UpdateService.Hash(File.ReadAllBytes(backup))==UpdateService.Hash(old),
                "atomic replacement retains a recoverable previous executable");
            ApplyEndToEnd(directory,key,xml,false);
            ApplyEndToEnd(directory,key,xml,true);
        }
        return count;
    }

    static void ApplyEndToEnd(string directory, RSACryptoServiceProvider key, string publicXml, bool failNewVersion)
    {
        string actualRoot=Path.Combine(directory,failNewVersion ? "helper-test-rollback" : "helper-test");Directory.CreateDirectory(actualRoot);
        string source=Path.Combine(actualRoot,"fixture.cs"), target=Path.Combine(actualRoot,"client.exe");
        string stub="using System;using System.IO;using System.Threading;namespace NexusRobloxManager {"+
            "public static class AppVersion{public const string Value=\"1.0.0.0\";}"+
            "public static class UpdateTrust{public const string PublicKeyXml=\""+publicXml+"\";}"+
            "public static class WatchdogEngine{public static string DataDir{get{return Environment.GetEnvironmentVariable(\"NX_UPDATE_TEST_ROOT\")??Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);}}}"+
            "public static class Program{public static void WriteDiagnostic(string s){File.AppendAllText(Path.Combine(WatchdogEngine.DataDir,\"helper.log\"),s);}"+
            "[STAThread]public static void Main(string[] args){if(args.Length==0){File.WriteAllText(Path.Combine(WatchdogEngine.DataDir,\"old-restarted\"),\"yes\");return;}if(args[0]==\"--apply-update\"){Environment.ExitCode=UpdateService.Apply(args[1],false);return;}"+
            "File.WriteAllText(Path.Combine(WatchdogEngine.DataDir,\"ready\"),\"yes\");while(!File.Exists(Path.Combine(WatchdogEngine.DataDir,\"exit\")))Thread.Sleep(20);}}}";
        File.WriteAllText(source,stub);
        string productionSource=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"src","UpdateService.cs");
        Compile("/out:"+Q(target)+" /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "+Q(source)+" "+Q(productionSource));
        string newSource=Path.Combine(actualRoot,"new.cs"), newBinary=Path.Combine(actualRoot,"new-client.exe");
        File.WriteAllText(newSource,"using System;using System.IO;class N{static void Main(){File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(\"NX_UPDATE_TEST_ROOT\"),\"new-started\"),\"yes\");if(Environment.GetEnvironmentVariable(\"NX_UPDATE_TEST_FAIL\")==\"1\")Environment.ExitCode=42;}}");
        Compile("/out:"+Q(newBinary)+" "+Q(newSource));
        byte[] old=File.ReadAllBytes(target), next=File.ReadAllBytes(newBinary);
        string cache=Path.Combine(actualRoot,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cache);
        string helper=Path.Combine(cache,"helper.exe"), requestPath=Path.Combine(cache,"request.json");
        File.WriteAllBytes(helper,old);File.WriteAllBytes(Path.Combine(cache,"new.exe"),next);
        string settings=Path.Combine(actualRoot,"settings-sentinel.json");File.WriteAllText(settings,"preserve user settings");
        var start=new ProcessStartInfo(target,"--wait") {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};start.EnvironmentVariables["NX_UPDATE_TEST_ROOT"]=actualRoot;
        using(var parent=Process.Start(start))
        {
            Process installer=null;
            try
            {
                Stopwatch ready=Stopwatch.StartNew();while(!File.Exists(Path.Combine(actualRoot,"ready")) && ready.ElapsedMilliseconds<10000)Thread.Sleep(20);
                Assert(File.Exists(Path.Combine(actualRoot,"ready")),"old update fixture starts without touching Roblox");
                var request=new UpdateApplyRequest {ParentPid=parent.Id,ParentStartTicks=parent.StartTime.ToUniversalTime().Ticks,TargetPath=target,
                    OldSha256=UpdateService.Hash(old),EnvelopeJson=Envelope(key,next,"Auto-Rejoin-PC","https://example.com/new.exe")};
                File.WriteAllText(requestPath,new JavaScriptSerializer().Serialize(request));
                var apply=new ProcessStartInfo(helper,"--apply-update "+Q(requestPath)) {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};apply.EnvironmentVariables["NX_UPDATE_TEST_ROOT"]=actualRoot;
                if(failNewVersion)apply.EnvironmentVariables["NX_UPDATE_TEST_FAIL"]="1";
                installer=Process.Start(apply);Thread.Sleep(300);
                Assert(UpdateService.Hash(File.ReadAllBytes(target))==UpdateService.Hash(old),"updater waits for the old manager to exit before replacing its image");
                File.WriteAllText(Path.Combine(actualRoot,"exit"),"yes");
                bool ended=parent.WaitForExit(10000) && installer.WaitForExit(20000);
                if(!failNewVersion)
                {
                    Assert(ended && installer.ExitCode==0,"real updater helper applies update after parent exits");
                    Assert(UpdateService.Hash(File.ReadAllBytes(target))==UpdateService.Hash(next) && File.Exists(Path.Combine(actualRoot,"new-started")),"updated executable actually starts");
                    Assert(File.ReadAllText(settings)=="preserve user settings" && UpdateService.Hash(File.ReadAllBytes(target+".previous"))==UpdateService.Hash(old),"update preserves user settings and old executable backup");
                }
                else
                {
                    Stopwatch recovery=Stopwatch.StartNew();while(!File.Exists(Path.Combine(actualRoot,"old-restarted")) && recovery.ElapsedMilliseconds<10000)Thread.Sleep(20);
                    Assert(ended && installer.ExitCode==1 && UpdateService.Hash(File.ReadAllBytes(target))==UpdateService.Hash(old),"failed new version restores the previous executable");
                    Assert(File.Exists(Path.Combine(actualRoot,"old-restarted")) && File.ReadAllText(settings)=="preserve user settings","rollback restarts the old version and retains user settings");
                }
            }
            finally
            {
                if(!parent.HasExited)parent.Kill();
                if(installer!=null){if(!installer.HasExited)installer.Kill();installer.Dispose();}
            }
        }
    }
}
