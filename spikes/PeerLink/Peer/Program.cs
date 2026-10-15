using PeerLinkSpike;

if (args[0] == "members")
{
    foreach (var t in new[] { typeof(KcpSharp.KcpConversation), typeof(KcpSharp.KcpConversationOptions), typeof(KcpSharp.IKcpTransport) })
        foreach (var m in t.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
            Console.WriteLine($"{t.Name} {m}");
    return;
    foreach (var m in typeof(SIPSorcery.Net.RtpIceChannel).GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
        Console.WriteLine(m);
    foreach (var m in typeof(SIPSorcery.Net.RTPChannel).GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
        Console.WriteLine("RTPChannel " + m);
    foreach (var m in typeof(SIPSorcery.Net.RTCIceCandidate).GetMembers(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
        Console.WriteLine("Candidate " + m);
    return;
}
if (args[0] == "probe")
{
    foreach (var type in new[] { typeof(SIPSorcery.Net.SctpDataSender), typeof(SIPSorcery.Net.SctpDataReceiver), typeof(SIPSorcery.Net.SctpAssociation), typeof(SIPSorcery.Net.RTCSctpTransport), typeof(SIPSorcery.Net.SctpTransport) })
        foreach (var f in type.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            Console.WriteLine($"{type.Name}.{f.Name} = {f.GetValue(null)} {(f.IsLiteral ? "const" : f.IsInitOnly ? "readonly" : "mutable")}");
    return;
}
// peer <signal url> answer <id>
// peer <signal url> call <id> <target> [megabytes]
var signal = new SignalClient(args[0], args[2]);
await signal.ConnectAsync();
void Log(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
if (args[1] == "kanswer")
{
    Log($"answering over ICE + KCP as {args[2]}");
    var link = await IceKcpSpike.ConnectAsync(signal, null, Log);
    Log($"linked: {link.Route}");
    await IceKcpSpike.ReceiveAsync(link, Log);
    await Task.Delay(2000);
    return;
}
if (args[1] == "kcall")
{
    var link = await IceKcpSpike.ConnectAsync(signal, args[3], Log);
    await IceKcpSpike.SendAsync(link, args.Length > 4 ? int.Parse(args[4]) : 20, Log);
    return;
}
if (args[1] == "answer")
{
    PeerSpike.Answer(signal, Log);
    Log($"answering as {args[2]}");
    await Task.Delay(Timeout.Infinite);
}
else
{
    await PeerSpike.CallAsync(signal, args[3], args.Length > 4 ? int.Parse(args[4]) : 20, Log);
    await signal.DisposeAsync();
}
