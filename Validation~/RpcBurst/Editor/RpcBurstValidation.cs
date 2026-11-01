#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

public static class RpcBurstValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string SessionKey = "TSMP.RpcBurst";
    const string ProbePath = "Assets/Validation/RpcBurst/RpcBurstProbe.asset";
    static string Root => Environment.GetEnvironmentVariable("TSMP_RPC_BURST_RESULTS");
    static readonly List<Object> owned = new List<Object>();
    static readonly List<Program> programs = new List<Program>();
    static readonly List<string> notes = new List<string>(), hashes = new List<string>();
    static Dictionary<string, string> before;
    static readonly List<Sample> vm = new List<Sample>(30000);
    static readonly List<string> enqueueRows = new List<string> { "case,id,time,accepted,queueBefore,queueAfter" };
    static readonly List<string> packetRows = new List<string> { "case,publication,time,frameIndex,bytes,rpcIds,queueBefore,queueAfter,headBefore,headAfter,marker,budget,packet,header" };
    static readonly List<string> frameRows = new List<string> { "case,frame,time,queue,headRepeats,pending,eventCount,markerCount,latestMarker,ageMs,budget" };
    static readonly List<string> eventRows = new List<string> { "case,id,enqueue,firstSend,apply,latencyMs,copies" };
    static readonly List<string> markerRows = new List<string> { "case,id,send,apply,latencyMs" };
    static readonly List<string> summaryRows = new List<string> { "case,begin,end,published,deadlineMisses,accepted,rejected,applied,markers,maxQueue,blockedOpportunities" };
    static IEnumerator work;
    static Action step;
    static int checks, depth;
    static Exception pumpError;
    static bool record;
    static string caseName;
    struct Sample { public string name, role, label; public int frame; public double time; public long start, end; public string Csv() => Row(name, role, label, frame, time, start, end, Ms(end-start)); }
    [Serializable] public sealed class Plan { public int sourceHz=30,renderHz=90,repeats=4; public Case[] cases; }
    [Serializable] public sealed class Case { public string name; public int burst; public double inputHz,inputSeconds,seconds; public bool mixed,blocked; }
    static void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
    static string H(byte[] bytes) { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static string Q(object v) => "\"" + (v is float ? ((float)v).ToString("R",CultureInfo.InvariantCulture) : v is double ? ((double)v).ToString("R",CultureInfo.InvariantCulture) : Convert.ToString(v,CultureInfo.InvariantCulture)).Replace("\"","\"\"") + "\"";
    static string Row(params object[] values) => string.Join(",", values.Select(Q));
    static double Ms(long ticks) => ticks*1000.0/Stopwatch.Frequency;
    static T Keep<T>(T value) where T:Object { owned.Add(value);return value; }

    static Dictionary<string, string> Sources()
    {
        var result = new Dictionary<string, string>();
        foreach (string package in new[] { "core", "codec.luma4", "codec.rgb16", "codec.color256", "codec.rgb20" })
        {
            string directory = "Packages/com.kibalab.tsmp." + package;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(directory);
            foreach (string scope in new[] { "Runtime", "Editor", "Shaders" })
            {
                string source = Path.Combine(info != null ? info.resolvedPath : directory, scope);
                if (!Directory.Exists(source)) continue;
                foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                    .Where(path => new[] { ".cs", ".shader", ".cginc", ".meta", ".asmdef", ".mat", ".prefab" }.Contains(Path.GetExtension(path))).OrderBy(path => path)) result[Path.GetFullPath(path)] = H(File.ReadAllBytes(path));
            }
        }
        return result;
    }
    public static void Run()
    {
        Check(!string.IsNullOrWhiteSpace(Root),"Results required"); Directory.CreateDirectory(Root);
        Check(!File.Exists(Path.Combine(Root,"result.txt")),"Fresh results required");
        File.WriteAllText(Path.Combine(Root,"result.txt"),"RUNNING");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var asset=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbePath);
        if(asset==null) { asset=ScriptableObject.CreateInstance<UdonSharpProgramAsset>(); asset.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/RpcBurst/Runtime/RpcBurstProbe.cs"); Check(asset.sourceCsScript!=null,"Probe missing"); AssetDatabase.CreateAsset(asset,ProbePath); }
        Compile(); AssetDatabase.SaveAssets(); SessionState.SetBool(SessionKey,true); Attach(); EditorApplication.EnterPlaymode();
    }
    static void Compile()
    {
        bool failed=false; Application.LogCallback listener=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)failed=true;};
        Application.logMessageReceived+=listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions{IsEditorBuild=false}); }
        finally { Application.logMessageReceived-=listener; }
        Check(!failed,"Client compilation failed");
    }
    [InitializeOnLoadMethod] static void Attach() { EditorApplication.playModeStateChanged-=Entered; EditorApplication.playModeStateChanged+=Entered; }
    static void Entered(PlayModeStateChange state) { if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(SessionKey,false))return; SessionState.SetBool(SessionKey,false); work=Validate(); EditorApplication.update+=Tick; }
    static void Tick() { if(pumpError!=null){var error=pumpError;pumpError=null;Finish(error);return;}try{if(work.MoveNext())return;}catch(Exception e){Finish(e);return;} Finish(null); }
    static void Dump()
    {
        File.WriteAllLines(Path.Combine(Root,"vm-events.csv"),new[]{"case,owner,event,frame,time,startTicks,endTicks,vmMs"}.Concat(vm.Select(s=>s.Csv())));
        File.WriteAllLines(Path.Combine(Root,"enqueue.csv"),enqueueRows); File.WriteAllLines(Path.Combine(Root,"packets.csv"),packetRows);
        File.WriteAllLines(Path.Combine(Root,"frames.csv"),frameRows); File.WriteAllLines(Path.Combine(Root,"events.csv"),eventRows);
        File.WriteAllLines(Path.Combine(Root,"markers.csv"),markerRows); File.WriteAllLines(Path.Combine(Root,"summary.csv"),summaryRows);
    }
    static void Finish(Exception error)
    {
        RpcBurstPump.FrameStep=null; step=null;record=false;EditorApplication.update-=Tick;
        foreach(var p in programs)p.Dispose(); programs.Clear(); RenderTexture.active=null;
        foreach(Object o in owned.AsEnumerable().Reverse()) { if(o is RenderTexture t)t.Release();if(o!=null)Object.DestroyImmediate(o); } owned.Clear();
        try { var after=Sources(); File.WriteAllLines(Path.Combine(Root,"source-after.csv"),new[]{"path,sha256"}.Concat(after.Select(p=>Row(p.Key,p.Value)))); Check(before!=null&&before.Count==after.Count&&before.All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value),"Product source changed"); } catch(Exception e){if(error==null)error=e;else notes.Add(e.ToString());}
        if(error!=null)notes.Add(error.ToString());notes.Add("Assertions="+checks);Dump();File.WriteAllLines(Path.Combine(Root,"artifact-hashes.csv"),hashes);
        File.WriteAllLines(Path.Combine(Root,"result.txt"),new[]{error==null?"PASS":"FAIL"}.Concat(notes));EditorApplication.Exit(error==null?0:1);
    }
    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;
        public string role;
        public Program(string path, TSMPCodec codec = null, string role = null)
        {
            this.role = role;
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            Check(asset != null, "Missing program " + path);
            Code = asset.SerializedProgramAsset.RetrieveProgram();
            Check(Code != null && Code.ByteCode.Length > 0, "Missing bytecode " + path);
            hashes.Add(Row("bytecode", path, H(Code.ByteCode)));
            var vm = UdonEditorManager.Instance.ConstructUdonVM(); Check(vm.LoadProgram(Code), "VM load failed.");
            Backing = new GameObject(asset.name + " refine cost VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Unresolved heap.");
            type.GetField("_program", Private).SetValue(Backing, Code);
            type.GetField("_udonVM", Private).SetValue(Backing, role != null ? new TimedVm(vm, Code, this) : vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true); type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols()) events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null)
                foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address)) Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
            programs.Add(this);
        }
        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM error in " + name);
        }
        public void Dispose() { if (Backing != null) Object.DestroyImmediate(Backing.gameObject); }
    }

    sealed class TimedVm:IUdonVM
    {
        readonly IUdonVM inner;readonly Program owner;readonly Dictionary<uint,string> labels=new Dictionary<uint,string>();
        public TimedVm(IUdonVM inner,IUdonProgram code,Program owner){this.inner=inner;this.owner=owner;foreach(string n in code.EntryPoints.GetExportedSymbols())labels[code.EntryPoints.GetAddressFromSymbol(n)]=n;}
        public bool DebugLogging{get=>inner.DebugLogging;set=>inner.DebugLogging=value;}
        public bool LoadProgram(IUdonProgram p)=>inner.LoadProgram(p);public IUdonProgram RetrieveProgram()=>inner.RetrieveProgram();public void SetProgramCounter(uint c)=>inner.SetProgramCounter(c);public uint GetProgramCounter()=>inner.GetProgramCounter();public IUdonHeap InspectHeap()=>inner.InspectHeap();
        public uint Interpret(){bool top=depth++==0;string label; if(!labels.TryGetValue(inner.GetProgramCounter(),out label))label="unknown";long begin=Stopwatch.GetTimestamp();double time=Time.realtimeSinceStartupAsDouble;uint result;try{result=inner.Interpret();}finally{depth--;}long end=Stopwatch.GetTimestamp();if(top&&record)vm.Add(new Sample{name=caseName,role=owner.role,label=label,frame=Time.frameCount,time=time,start=begin,end=end});return result;}
    }
    static TSMPCodec CloneCodec(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab");
        Check(prefab != null, "Missing codec prefab " + name);
        var codec = Keep(Object.Instantiate(prefab)).GetComponent<TSMPCodec>();
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> clone = original =>
        {
            if (original == null) return null;
            Material copy; if (copies.TryGetValue(original, out copy)) return copy;
            copy = Keep(new Material(original)); copies.Add(original, copy); return copy;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, clone((Material)field.GetValue(codec)));
            else if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] materials) field.SetValue(codec, materials.Select(clone).ToArray());
        }
        return codec;
    }
    static RenderTexture Target(int width, int height)
    {
        var texture = Keep(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        Check(texture.Create(), "RenderTexture allocation failed."); return texture;
    }
    static void Bind(Program owner,Program target)
    {
        var field=TransSyncMetadata.GetOrCreate(null,typeof(RpcBurstProbe)).Fields.Single();
        owner.Set("bindingTargets",new Component[0]);owner.Set("bindingUdonTargets",new[]{target.Backing});owner.Set("bindingNetworkIds",new ushort[]{1});owner.Set("bindingVariableHashes",new[]{field.VariableHash});owner.Set("bindingValueTypes",new[]{(byte)field.ValueType});owner.Set("bindingFieldNames",new[]{nameof(RpcBurstProbe.marker)});owner.Set("bindingDirections",new[]{0});owner.Set("bindingPriorities",new[]{0});
    }
    static IEnumerator Validate()
    {
        Compile();Application.runInBackground=true;QualitySettings.vSyncCount=0;before=Sources();File.WriteAllLines(Path.Combine(Root,"source-before.csv"),new[]{"path,sha256"}.Concat(before.Select(p=>Row(p.Key,p.Value))));
        notes.Add("Unity="+Application.unityVersion+"; GPU="+SystemInfo.graphicsDeviceName+"; API="+SystemInfo.graphicsDeviceType+"; ColorSpace="+QualitySettings.activeColorSpace+"; CPU="+SystemInfo.processorType+"; StopwatchFrequency="+Stopwatch.Frequency);
        notes.Add("Actual post-Play client Encoder/Luma4/Decoder/receiver VM, actual VRC GPU readback callbacks, local empty Editor scene; no real VRChat/CDN result. Timings include validation receiver VM body, host packet parsing/files excluded. Source clock and receiver float timestamps differ in precision.");
        hashes.Add("kind,path,sha256");var bytes=File.ReadAllBytes(Environment.GetEnvironmentVariable("TSMP_RPC_BURST_PLAN"));File.WriteAllBytes(Path.Combine(Root,"plan-used.json"),bytes);hashes.Add(Row("plan","plan-used.json",H(bytes)));var plan=JsonUtility.FromJson<Plan>(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        Check(plan.cases!=null&&plan.cases.Length>0&&plan.sourceHz==30&&plan.repeats==4,"Frozen default plan invalid");Keep(new GameObject("RPC pump")).AddComponent<RpcBurstPump>();RpcBurstPump.FrameStep=()=>{try{step?.Invoke();}catch(Exception error){pumpError=error;step=null;}};Keep(new GameObject("Camera")).AddComponent<Camera>().cullingMask=0;
        foreach(var item in plan.cases){var run=Measure(item,plan);while(run.MoveNext())yield return run.Current;Dump();yield return null;GC.Collect();}
    }
    static List<int> PacketRpcIds(byte[] bytes,int length)
    {
        var ids=new List<int>();int cursor=8,messages=Binary.ReadUInt16LE(bytes,2);
        for(int i=0;i<messages;i++){int end=cursor+8+Binary.ReadUInt16LE(bytes,cursor+6);Check(end<=length,"Packet bounds");if(bytes[cursor+2]==NetworkFrameProtocol.MessageTypeRpcCall){int arg=cursor+13;Check(bytes[cursor+12]==2,"RPC argument count");arg+=3+Binary.ReadUInt16LE(bytes,arg+1);ids.Add(Binary.ReadInt32LE(bytes,arg+3));}cursor=end;}
        Check(cursor==length,"Packet tail");return ids;
    }
    static IEnumerator Measure(Case item,Plan plan)
    {
        int startPrograms=programs.Count,startOwned=owned.Count;caseName=item.name;record=false;Application.targetFrameRate=plan.renderHz;
        var source=Target(1280,720);var output=Target(2048,1);var native=CloneCodec("Luma4");var codec=new Program("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset",native);
        var sender=new Program(ProbePath);var receiver=new Program(ProbePath);var encoder=new Program("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset",role:"Encoder");var decoder=new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset",role:"Decoder");
        sender.Set("networkId",(ushort)1);sender.Set("transRpcEncoder",encoder.Backing);receiver.Set("networkId",(ushort)1);receiver.Set("rpcDecoder",decoder.Backing);receiver.Set("eventIds",new int[2048]);receiver.Set("eventTimes",new float[2048]);receiver.Set("markers",new int[4096]);receiver.Set("markerTimes",new float[4096]);
        encoder.Set("output",source);encoder.Set("autoEncode",false);encoder.Set("sampleSize",4);encoder.Set("frameIndex",1u);encoder.Set("blockExpandMaterial",Keep(new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"))));encoder.Set("luma4EncodeMaterial",Resources.Load<Material>("TSMPEncodeLuma4"));encoder.Set("useGpuLuma4",true);encoder.Set("selectedCodecUdonTarget",codec.Backing);encoder.Set("debugLog",false);Bind(encoder,sender);encoder.Set("bindingSendOnChange",new[]{!item.mixed});encoder.Set("bindingMinSendIntervals",new[]{0f});encoder.Set("targetPayloadBytes",item.blocked?256:0);encoder.Call("_onEnable");
        decoder.Set<Texture>("sourceTexture",source);decoder.Set("payloadByteTexture",output);decoder.Set("readbackPackMaterial",Resources.Load<Material>("TSMPReadbackPack"));decoder.Set("codecHandlers",new[]{codec.Backing});Bind(decoder,receiver);decoder.Set("applyEveryFrame",true);decoder.Set("skipDuplicateFrames",true);decoder.Set("requirePayloadCrc32",true);decoder.Set("debugLog",false);decoder.Set("flipY",true);decoder.Set("recoverHeaderCrc",false);decoder.Set("maxCrcRecoveryAttempts",2);decoder.Set("usePredictedReadback",true);decoder.Set("useCombinedByteOutput",true);decoder.Set("overlapReadbacks",true);decoder.Call("_onEnable");
        Check(encoder.Get<int>("transRpcRepeatFrames")==plan.repeats&&encoder.Get<float>("transSyncRefreshInterval")==1f,"Product defaults changed");
        sender.Set("marker",0);encoder.Call("EncodeNow");decoder.Call("DecodeNow");double timeout=Time.realtimeSinceStartupAsDouble+30;while(decoder.Get<bool>("readbackInFlight")&&Time.realtimeSinceStartupAsDouble<timeout)yield return null;
        Check(!decoder.Get<bool>("readbackInFlight")&&receiver.Get<int>("markerCount")==1,"GPU preflight failed: "+decoder.Get<string>("lastError"));
        var enqueued=new Dictionary<int,double>();var firstSend=new Dictionary<int,double>();var copies=new Dictionary<int,int>();var sentMarkers=new Dictionary<int,double>{{0,Time.realtimeSinceStartupAsDouble}};var acceptedIds=new List<int>();
        int nextId=0,publications=0,misses=0,rejected=0,maxQueue=0,blockedOpportunities=0,latestMarker=0;bool burstQueued=false;double begin=Time.realtimeSinceStartupAsDouble+1,end=begin+item.seconds,due=begin,inputDue=begin;
        string packetDirectory=Path.Combine(Root,"packets",caseName);Directory.CreateDirectory(packetDirectory);
        Action enqueue=()=>{int id=++nextId,q=encoder.Get<int>("_pendingRpcCount");double stamp=Time.realtimeSinceStartupAsDouble;sender.Set("nextEventId",id);sender.Call("QueueEvent");bool accepted=sender.Get<bool>("accepted");enqueueRows.Add(Row(caseName,id,stamp,accepted,q,encoder.Get<int>("_pendingRpcCount")));if(accepted){acceptedIds.Add(id);enqueued[id]=stamp;copies[id]=0;}else rejected++;};
        step=()=>
        {
            double now=Time.realtimeSinceStartupAsDouble;if(now<begin)return;record=true;
            if(now<end)
            {
                if(!burstQueued){for(int i=0;i<item.burst;i++)enqueue();burstQueued=true;}
                if(item.inputHz>0&&now<begin+item.inputSeconds&&now>=inputDue){int slots=Math.Max(1,(int)Math.Floor((now-inputDue)*item.inputHz)+1);inputDue+=slots/item.inputHz;for(int i=0;i<slots;i++)enqueue();}
                if(item.blocked)encoder.Set("targetPayloadBytes",now<begin+1.2?29:256);
                maxQueue=Math.Max(maxQueue,encoder.Get<int>("_pendingRpcCount"));
                if(now>=due)
                {
                    int slots=Math.Max(1,(int)Math.Floor((now-due)*plan.sourceHz)+1);misses+=slots-1;due+=slots/(double)plan.sourceHz;if(item.mixed)latestMarker++;sender.Set("marker",latestMarker);
                    int queueBefore=encoder.Get<int>("_pendingRpcCount"),headBefore=queueBefore>0?encoder.Get<int[]>("_pendingRpcRepeatsRemaining")[0]:0;uint frameBefore=encoder.Get<uint>("frameIndex");double stamp=Time.realtimeSinceStartupAsDouble;encoder.Call("EncodeNow");
                    Check(string.IsNullOrEmpty(encoder.Get<string>("lastError")),"Encode failed "+encoder.Get<string>("lastError"));uint afterFrame=encoder.Get<uint>("frameIndex");
                    if(afterFrame!=frameBefore)
                    {
                        publications++;Check(encoder.Get<bool>("lastFrameUsedGpuLuma4"),"Not actual GPU Luma4");byte[] header=(byte[])encoder.Get<byte[]>("_headerBytes").Clone();int encodedLength=Binary.ReadUInt16LE(header,42);byte[] packet=encoder.Get<byte[]>("_payloadBytes").Take(encodedLength).ToArray();var ids=PacketRpcIds(packet,packet.Length);foreach(int id in ids){Check(copies.ContainsKey(id),"Rejected/unknown event emitted");copies[id]++;if(!firstSend.ContainsKey(id))firstSend[id]=stamp;}
                        string relative=Path.Combine("packets",caseName,publications.ToString("D4")+".bin"),relativeHeader=relative+".header.bin";File.WriteAllBytes(Path.Combine(Root,relative),packet);File.WriteAllBytes(Path.Combine(Root,relativeHeader),header);int queueAfter=encoder.Get<int>("_pendingRpcCount");packetRows.Add(Row(caseName,publications,stamp,frameBefore,packet.Length,string.Join(";",ids),queueBefore,queueAfter,headBefore,queueAfter>0?encoder.Get<int[]>("_pendingRpcRepeatsRemaining")[0]:0,latestMarker,encoder.Get<int>("targetPayloadBytes"),relative,relativeHeader));if(item.mixed)sentMarkers[latestMarker]=stamp;
                    }
                    if(item.blocked&&now<begin+1.2){blockedOpportunities++;Check(encoder.Get<int>("_pendingRpcCount")==acceptedIds.Count&&encoder.Get<int[]>("_pendingRpcRepeatsRemaining").Take(acceptedIds.Count).All(r=>r==plan.repeats),"Blocked event consumed");}
                }
            }
            decoder.Call("_update");int markerCount=receiver.Get<int>("markerCount");int marker=markerCount>0?receiver.Get<int[]>("markers")[markerCount-1]:0;double age=marker>0&&sentMarkers.ContainsKey(marker)?(Time.realtimeSinceStartupAsDouble-sentMarkers[marker])*1000:-1;int queue=encoder.Get<int>("_pendingRpcCount");frameRows.Add(Row(caseName,Time.frameCount,now,queue,queue>0?encoder.Get<int[]>("_pendingRpcRepeatsRemaining")[0]:0,decoder.Get<int>("pendingDecodeCount"),receiver.Get<int>("eventCount"),markerCount,marker,age,encoder.Get<int>("targetPayloadBytes")));
        };
        while(Time.realtimeSinceStartupAsDouble<end+0.5)yield return null;step=null;record=false;decoder.Set("applyEveryFrame",false);timeout=Time.realtimeSinceStartupAsDouble+10;while(decoder.Get<bool>("readbackInFlight")&&Time.realtimeSinceStartupAsDouble<timeout)yield return null;Check(!decoder.Get<bool>("readbackInFlight"),"GPU drain timeout");
        Check(encoder.Get<int>("_pendingRpcCount")==0,"Accepted queue did not drain");Check(copies.Values.All(c=>c==plan.repeats),"Incorrect repeat count");int count=receiver.Get<int>("eventCount");int[] actual=receiver.Get<int[]>("eventIds").Take(count).ToArray();Check(actual.SequenceEqual(acceptedIds),"Missing/duplicate/out-of-order receiver event: "+string.Join(";",actual));float[] applied=receiver.Get<float[]>("eventTimes");for(int i=0;i<count;i++){int id=actual[i];eventRows.Add(Row(caseName,id,enqueued[id],firstSend[id],applied[i],(applied[i]-enqueued[id])*1000,copies[id]));}
        int markerN=receiver.Get<int>("markerCount");int[] markerIds=receiver.Get<int[]>("markers");float[] markerTimes=receiver.Get<float[]>("markerTimes");int previous=-1;for(int i=0;i<markerN;i++){int id=markerIds[i];Check((item.mixed?id>previous:id==0)&&sentMarkers.ContainsKey(id),"Invalid state marker");previous=id;markerRows.Add(Row(caseName,id,item.mixed?sentMarkers[id]:-1,markerTimes[i],item.mixed?(markerTimes[i]-sentMarkers[id])*1000:-1));}
        Check(item.burst!=33||acceptedIds.Count==32&&rejected==1,"Queue-full boundary wrong");summaryRows.Add(Row(caseName,begin,end,publications,misses,acceptedIds.Count,rejected,count,markerN,maxQueue,blockedOpportunities));
        decoder.Call("_onDisable");encoder.Call("_onDisable");foreach(var p in programs.Skip(startPrograms).ToArray()){p.Dispose();programs.Remove(p);}foreach(var o in owned.Skip(startOwned).Reverse().ToArray()){if(o is RenderTexture t)t.Release();if(o!=null)Object.DestroyImmediate(o);owned.Remove(o);}notes.Add("PASS "+caseName+"; accepted="+acceptedIds.Count+"; rejected="+rejected+"; first-to-last-send-span="+(firstSend.Count>0?firstSend.Values.Max()-firstSend.Values.Min():0));UnityEngine.Debug.Log("RPC burst complete "+caseName);
    }
}
#endif
