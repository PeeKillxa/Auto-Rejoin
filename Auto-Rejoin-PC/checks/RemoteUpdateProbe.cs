using System;using System.Net;using System.Text;using NexusRobloxManager;
class RemoteUpdateProbe { static void Main(){
 var newer=UpdateService.Check();
 using(var web=new WebClient()){
  string json=web.DownloadString(UpdateService.FeedUrl+"?verify="+DateTime.UtcNow.Ticks);
  var m=UpdateService.VerifyManifest(json,UpdateTrust.PublicKeyXml);
  UpdateService.VerifyBinary(web.DownloadData(m.url),m);
  Console.WriteLine("REMOTE VERIFIED channel="+m.channel+" version="+m.version+" bytes="+m.size+" availableNewer="+(newer!=null));
 }
}}
