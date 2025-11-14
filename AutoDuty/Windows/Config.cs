using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using Serilog.Events;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static AutoDuty.Helpers.RepairNPCHelper;
using static AutoDuty.Windows.ConfigTab;

namespace AutoDuty.Windows;

using Data;
using ECommons.Automation;
using ECommons.Configuration;
using ECommons.ExcelServices;
using ECommons.PartyFunctions;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Properties;
using System;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Achievement = Lumina.Excel.Sheets.Achievement;
using ExitDutyHelper = Helpers.ExitDutyHelper;
using Map = Lumina.Excel.Sheets.Map;
using Thread = System.Threading.Thread;
using Vector2 = FFXIVClientStructs.FFXIV.Common.Math.Vector2;

[JsonObject(MemberSerialization.OptIn)]
public class ConfigurationMain
{
    public const string CONFIGNAME_BARE = "Bare";

    public static ConfigurationMain Instance;

    [JsonProperty]
    public string DefaultConfigName = CONFIGNAME_BARE;

    [JsonProperty]
    private string activeProfileName = CONFIGNAME_BARE;
    
    public  string ActiveProfileName => this.activeProfileName;

    public bool Initialized { get; private set; } = false;

    [JsonProperty]
    private readonly HashSet<ProfileData> profileData = [];

    private readonly Dictionary<string, ProfileData> profileByName = [];
    private readonly Dictionary<ulong, string> profileByCID = [];

    [JsonProperty]
    public readonly Dictionary<ulong, CharData> charByCID = [];

    [JsonObject(MemberSerialization.OptOut)]
    public struct CharData
    {
        public required ulong  CID;
        public          string Name;
        public          string World;

        public string GetName() => this.Name.Any() ? $"{this.Name}@{this.World}" : CID.ToString();

        public override int GetHashCode() => this.CID.GetHashCode();
    }

    [JsonProperty]
    //Dev Options
    internal bool updatePathsOnStartup = true;
    public bool UpdatePathsOnStartup
    {
        get => !Plugin.isDev || this.updatePathsOnStartup;
        set => this.updatePathsOnStartup = value;
    }

    internal bool multiBox = false;
    public bool MultiBox
    {
        get => this.multiBox;
        set
        {
            if (this.multiBox == value)
                return;
            this.multiBox = value;

            MultiboxUtility.Set(this.multiBox);
        }
    }

    [JsonProperty]
    internal bool host = false;

    public class MultiboxUtility
    {
        private const int    BUFFER_SIZE            = 4096;
        private const string PIPE_NAME              = "AutoDutyPipe";

        private const string SERVER_AUTH_KEY = "AD_Server_Auth!";
        private const string CLIENT_AUTH_KEY = "AD_Client_Auth!";
        private const string CLIENT_CID_KEY  = "CLIENT_CID";
        private const string PARTY_INVITE    = "PARTY_INVITE";

        private const string KEEPALIVE_KEY          = "KEEP_ALIVE";
        private const string KEEPALIVE_RESPONSE_KEY = "KEEP_ALIVE received";

        private const string DUTY_QUEUE_KEY = "DUTY_QUEUE";
        private const string DUTY_EXIT_KEY = "DUTY_EXIT";

        private const string DEATH_KEY   = "DEATH";
        private const string UNDEATH_KEY = "UNDEATH";
        private const string DEATH_RESET_KEY = "DEATH_RESET";

        private const string STEP_COMPLETED = "STEP_COMPLETED";
        private const string STEP_START     = "STEP_START";

        private static bool stepBlock = false;
        public static bool MultiboxBlockingNextStep
        {
            get
            {
                if (!Instance.MultiBox)
                    return false;

                return stepBlock;
            }
            set
            {
                if (!Instance.MultiBox)
                    return;

                if (!value)
                {
                    if (Instance.host)
                        Server.SendStepStart();
                }

                if (stepBlock == value)
                    return;

                stepBlock = value;

                if(stepBlock)
                    if (Instance.host)
                    {
                        Plugin.Action = "Waiting for clients";
                        Server.CheckStepProgress();
                    }
                    else
                    {
                        Client.SendStepCompleted();
                    }
            }
        }

        public static void IsDead(bool dead)
        {
            if (Instance.MultiBox)
                return;

            if(!Instance.host)
                Client.SendDeath(dead);
            else
                Server.CheckDeaths();
        }

        public static void Set(bool on)
        {
            if(on)
                Instance.GetCurrentConfig.DutyModeEnum = DutyMode.Regular;

            if (Instance.host)
                Server.Set(on);
            else
                Client.Set(on);
        }

        internal static class Server
        {
            public const             int             MAX_SERVERS = 3;
            private static readonly  Thread?[]       threads     = new Thread?[MAX_SERVERS];
            private static readonly  StreamString?[] streams     = new StreamString?[MAX_SERVERS];
            internal static readonly ClientInfo?[]   clients        = new ClientInfo?[MAX_SERVERS];

            internal static readonly DateTime[] keepAlives    = new DateTime[MAX_SERVERS];
            private static readonly  bool[]     stepConfirms  = new bool[MAX_SERVERS];
            private static readonly  bool[]     deathConfirms = new bool[MAX_SERVERS];

            private static readonly NamedPipeServerStream?[] pipes = new NamedPipeServerStream[MAX_SERVERS];

            public static void Set(bool on)
            {
                try
                {
                    if (on)
                    {
                        for (int i = 0; i < MAX_SERVERS; i++)
                        {
                            threads[i] = new Thread(ServerThread);
                            threads[i]?.Start(i);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < MAX_SERVERS; i++)
                        {
                            if (pipes[i]?.IsConnected ?? false)
                                pipes[i]?.Disconnect();
                            pipes[i]?.Close();
                            pipes[i]?.Dispose();
                            threads[i] = null;
                            streams[i] = null;
                            clients[i] = null;

                            keepAlives[i]   = DateTime.MinValue;
                            stepConfirms[i] = false;

                            Chat.ExecuteCommand("/partycmd breakup");

                            SchedulerHelper.ScheduleAction("MultiboxServer PartyBreakup Accept", () =>
                            {
                                unsafe
                                {
                                    Utf8String inviterName = InfoProxyPartyInvite.Instance()->InviterName;

                                    if(UniversalParty.Length <= 1)
                                    {
                                        SchedulerHelper.DescheduleAction("MultiboxServer PartyBreakup Accept");
                                        return;
                                    }


                                    if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) &&
                                        GenericHelpers.IsAddonReady(addonSelectYesno))
                                    {
                                        AddonMaster.SelectYesno yesno = new(addonSelectYesno);
                                        if (yesno.Text.Contains(inviterName.ToString()))
                                            yesno.Yes();
                                        else
                                            yesno.No();
                                    }
                                }
                            }, 500, false);
                        }
                        
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog(ex.ToString());
                }
            }


            private static void ServerThread(object? data)
            {
                try
                {
                    int index = (int)(data ?? throw new ArgumentNullException(nameof(data)));

                    NamedPipeServerStream pipeServer = new(PIPE_NAME, PipeDirection.InOut, MAX_SERVERS, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    pipes[index] = pipeServer;

                    int threadId = Thread.CurrentThread.ManagedThreadId;

                    DebugLog($"Server thread started with ID: {threadId}");
                    pipeServer.WaitForConnection();

                    DebugLog($"Client connected on ID: {threadId}");


                    StreamString ss = new(pipeServer);
                    ss.WriteString(SERVER_AUTH_KEY);

                    if (ss.ReadString() != CLIENT_AUTH_KEY)
                        return;

                    DebugLog($"Client authenticated on ID: {threadId}");
                    streams[index] = ss;

                    while (pipes[index]?.IsConnected ?? false)
                    {
                        string   message = ss.ReadString().Trim();
                        string[] split   = message.Split("|");


                        switch (split[0])
                        {
                            case CLIENT_CID_KEY:
                                clients[index] = new ClientInfo(ulong.Parse(split[1]), split[2], ushort.Parse(split[3]));

                                Svc.Framework.RunOnTick(() =>
                                                        {

                                                            unsafe
                                                            {
                                                                ClientInfo client = clients[index]!;
                                                                DebugLog($"Client Identification received: {client.CID} {client.CName} {client.WorldId}");

                                                                if (!PartyHelper.IsPartyMember(client.CID))
                                                                {
                                                                    if (client.WorldId == Player.CurrentWorldId)
                                                                        InfoProxyPartyInvite.Instance()->InviteToParty(client.CID, client.CName, client.WorldId);
                                                                    else
                                                                        InfoProxyPartyInvite.Instance()->InviteToPartyContentId(client.CID, 0);

                                                                    ss.WriteString(PARTY_INVITE);
                                                                }
                                                            }
                                                        });

                                break;
                            case KEEPALIVE_KEY:
                                ss.WriteString(KEEPALIVE_RESPONSE_KEY);
                                keepAlives[index] = DateTime.Now;
                                break;
                            case STEP_COMPLETED:
                                stepConfirms[index] = true;
                                CheckStepProgress();
                                break;
                            case DEATH_KEY:
                                deathConfirms[index] = true;
                                CheckDeaths();
                                break;
                            case UNDEATH_KEY:
                                deathConfirms[index] = false;
                                break;
                            default:
                                ss.WriteString($"Unknown Message: {message}");
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    DebugLog($"SERVER ERROR: {e.Message}\n{e.StackTrace}");
                }
            }

            public static bool AllInParty()
            {
                for (int i = 0; i < MAX_SERVERS; i++)
                {
                    if (clients[i] == null || !PartyHelper.IsPartyMember(clients[i]!.CID))
                        return false;
                }

                return true;
            }

            public static void CheckDeaths()
            {
                if (deathConfirms.All(x => x) && Player.IsDead)
                {
                    for (int i = 0; i < deathConfirms.Length; i++)
                        deathConfirms[i] = false;

                    DebugLog("All dead");
                    SendToAllClients(DEATH_RESET_KEY);
                }
                else
                {
                    DebugLog("Not all clients are dead yet, waiting for more death.");
                }
            }

            public static void CheckStepProgress()
            {
                if((Plugin.Stage != Stage.Looping && Plugin.Indexer >= 0 && Plugin.Indexer < Plugin.Actions.Count && Plugin.Actions[Plugin.Indexer].Tag == ActionTag.Treasure || stepConfirms.All(x => x)) &&
                   stepBlock)
                {
                    for (int i = 0; i < stepConfirms.Length; i++)
                        stepConfirms[i] = false;

                    DebugLog("All clients completed the step");
                    stepBlock = false;
                }
                else
                {
                    DebugLog("Not all clients have completed the step yet, waiting for more confirmations.");
                }
            }

            public static void SendStepStart()
            {
                DebugLog("Synchronizing Clients to Server step");
                SendToAllClients($"{STEP_START}|{Plugin.Indexer}");
            }

            public static void ExitDuty()
            {
                DebugLog("exiting duty");
                SendToAllClients(DUTY_EXIT_KEY);
            }

            public static void Queue()
            {
                DebugLog("Queue initiated");
                SendToAllClients(DUTY_QUEUE_KEY);
            }

            private static void SendToAllClients(string message)
            {
                foreach (StreamString? ss in streams) 
                    ss?.WriteString(message);
            }

            internal record ClientInfo(ulong CID, string CName, ushort WorldId);
        }

        private static class Client
        {
            private static Thread?                thread;
            private static NamedPipeClientStream? pipe;
            private static StreamString?          clientSS;

            public static void Set(bool on)
            {
                if (on)
                {
                    thread = new Thread(ClientThread);
                    thread.Start();
                }
                else
                {
                    pipe?.Close();
                    pipe?.Dispose();
                    clientSS = null;
                    thread   = null;
                }
            }


            private static void ClientThread(object? data)
            {
                try
                {
                    NamedPipeClientStream pipeClient = new(".", PIPE_NAME, PipeDirection.InOut, PipeOptions.Asynchronous);

                    pipe = pipeClient;

                    DebugLog("Connecting to server...\n");
                    pipeClient.Connect();

                    clientSS = new StreamString(pipeClient);

                    if (clientSS.ReadString() == SERVER_AUTH_KEY)
                    {
                        clientSS.WriteString(CLIENT_AUTH_KEY);

                        Svc.Framework.RunOnTick(() =>
                                                {
                                                    if (Player.CID != 0)
                                                        clientSS.WriteString($"{CLIENT_CID_KEY}|{Player.CID}|{Player.Name}|{Player.CurrentWorldId}");
                                                });


                        new Thread(ClientKeepAliveThread).Start();

                        while (pipe?.IsConnected ?? false)
                        {
                            string   message = clientSS.ReadString().Trim();
                            string[] split   = message.Split("|");

                            switch (split[0])
                            {
                                case STEP_START:
                                    if (int.TryParse(split[1], out int step))
                                    {
                                        Plugin.Indexer = step;
                                        stepBlock      = false;
                                    }
                                    break;
                                case KEEPALIVE_RESPONSE_KEY:
                                    break;
                                case DUTY_QUEUE_KEY:
                                    QueueHelper.InvokeAcceptOnly();
                                    break;
                                case DUTY_EXIT_KEY:
                                    ExitDutyHelper.Invoke();
                                    break;
                                case PARTY_INVITE:
                                    SchedulerHelper.ScheduleAction("MultiboxClient PartyInvite Accept", () =>
                                                                                                        {
                                                                                                            unsafe
                                                                                                            {
                                                                                                                Utf8String inviterName = InfoProxyPartyInvite.Instance()->InviterName;

                                                                                                                
                                                                                                                if (InfoProxyPartyInvite.Instance()->InviterWorldId != 0 && 
                                                                                                                    UniversalParty.Length <= 1 &&
                                                                                                                    GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) &&
                                                                                                                    GenericHelpers.IsAddonReady(addonSelectYesno))
                                                                                                                {
                                                                                                                    AddonMaster.SelectYesno yesno = new(addonSelectYesno);
                                                                                                                    if (yesno.Text.Contains(inviterName.ToString()))
                                                                                                                    {
                                                                                                                        yesno.Yes();
                                                                                                                        SchedulerHelper.DescheduleAction("MultiboxClient PartyInvite Accept");
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        yesno.No();
                                                                                                                    }
                                                                                                                }
                                                                                                            }
                                                                                                        }, 500, false);
                                    break;
                                default:
                                    ErrorLog("Unknown response: " + message);
                                    break;
                            }
                        }
                    }

                }
                catch (Exception e)
                {
                    DebugLog($"Client ERROR: {e.Message}\n{e.StackTrace}");
                }
            }

            private static void ClientKeepAliveThread(object? data)
            {
                try
                {
                    Thread.Sleep(1000);
                    while (pipe?.IsConnected ?? false)
                    {
                        clientSS?.WriteString(KEEPALIVE_KEY);
                        Thread.Sleep(10000);
                    }
                }
                catch (Exception e)
                {
                    DebugLog("Client KEEPALIVE Error: " + e);
                }
            }

            public static void SendStepCompleted()
            {
                if (clientSS == null || !(pipe?.IsConnected ?? false))
                {
                    DebugLog("Client not connected, cannot send step completed.");
                    return;
                }
                Plugin.Action = "Waiting for others";
                clientSS.WriteString(STEP_COMPLETED);
                DebugLog("Step completed sent to server.");
            }

            public static void SendDeath(bool dead)
            {
                if (clientSS == null || !(pipe?.IsConnected ?? false))
                {
                    DebugLog("Client not connected, cannot send death.");
                    return;
                }
                clientSS.WriteString(dead ? DEATH_KEY : UNDEATH_KEY);
                DebugLog("Death sent to server.");
            }
        }


        private static void DebugLog(string message)
        {
            Svc.Log.Debug($"Pipe Connection: {message}");
        }
        private static void ErrorLog(string message)
        {
            Svc.Log.Error($"Pipe Connection: {message}");
        }

        private class StreamString
        {
            private readonly Stream ioStream;
            private readonly UnicodeEncoding streamEncoding;

            public StreamString(Stream ioStream)
            {
                this.ioStream = ioStream;
                this.streamEncoding = new UnicodeEncoding();
            }

            public string ReadString()
            {
                int b1 = this.ioStream.ReadByte();
                int b2 = this.ioStream.ReadByte();

                if(b1 == -1)
                {
                    DebugLog("End of stream reached.");
                    return string.Empty;
                }

                int    len      = b1 * 256 + b2;
                byte[] inBuffer = new byte[len];
                this.ioStream.Read(inBuffer, 0, len);
                string readString = this.streamEncoding.GetString(inBuffer);

                DebugLog("Reading: " + readString);

                return readString;
            }

            public int WriteString(string outString)
            {
                DebugLog("Writing: " + outString);

                byte[] outBuffer = this.streamEncoding.GetBytes(outString);
                int len = outBuffer.Length;
                if (len > ushort.MaxValue)
                {
                    len = (int)ushort.MaxValue;
                }
                this.ioStream.WriteByte((byte)(len / 256));
                this.ioStream.WriteByte((byte)(len & 255));
                this.ioStream.Write(outBuffer, 0, len);
                this.ioStream.Flush();

                return outBuffer.Length + 2;
            }
        }
    }

    public IEnumerable<string> ConfigNames => this.profileByName.Keys;
     
    public ProfileData GetCurrentProfile
    {
        get
        {
            if (!this.profileByName.TryGetValue(this.ActiveProfileName, out ProfileData? profiles))
            {
                this.SetProfileToDefault();
                return this.GetCurrentProfile;
            }

            return profiles;
        }
    }

    public Configuration GetCurrentConfig => this.GetCurrentProfile.Config;

    public void Init()
    {
        if (this.profileData.Count == 0)
        {
            if (Svc.PluginInterface.ConfigFile.Exists)
            {
                Configuration? configuration = EzConfig.DefaultSerializationFactory.Deserialize<Configuration>(File.ReadAllText(Svc.PluginInterface.ConfigFile.FullName, Encoding.UTF8));
                if (configuration != null)
                {
                    this.CreateProfile("Migrated", configuration);
                    this.SetProfileAsDefault();
                }
            }
        }

        void RegisterProfileData(ProfileData profile)
        {
            if (profile.CIDs.Any())
                foreach (ulong cid in profile.CIDs)
                    this.profileByCID[cid] = profile.Name;
            this.profileByName[profile.Name] = profile;
        }

        foreach (ProfileData profile in this.profileData)
            if(profile.Name != CONFIGNAME_BARE)
                RegisterProfileData(profile);

        RegisterProfileData(new ProfileData
                            {
                                Name = CONFIGNAME_BARE,
                                Config = new Configuration
                                         {
                                             EnablePreLoopActions     = false,
                                             EnableBetweenLoopActions = false,
                                             EnableTerminationActions = false,
                                             LootTreasure             = false
                                         }
                            });

        this.SetProfileToDefault();
    }

    public bool SetProfile(string name)
    {
        DebugLog("Changing profile to: " + name);
        if (this.profileByName.ContainsKey(name))
        {
            this.activeProfileName = name;
            EzConfig.Save();
            return true;
        }
        return false;
    }

    public void SetProfileAsDefault()
    {
        if (this.profileByName.ContainsKey(this.ActiveProfileName))
        {
            this.DefaultConfigName = this.ActiveProfileName;
            EzConfig.Save();
        }
    }

    public void SetProfileToDefault()
    {
        this.SetProfile(CONFIGNAME_BARE);
        Svc.Framework.RunOnTick(() =>
        {
            DebugLog($"Setting to default profile for {Player.Name} ({Player.CID}) {PlayerHelper.IsValid}");

            if (Player.Available && this.profileByCID.TryGetValue(Player.CID, out string? charProfile))
                if (this.SetProfile(charProfile))
                    return;
            DebugLog("No char default found. Using general default");
            if (!this.SetProfile(this.DefaultConfigName))
            {
                DebugLog("Fallback, using bare");
                this.DefaultConfigName = CONFIGNAME_BARE;
                this.SetProfile(CONFIGNAME_BARE);
            }

            this.Initialized = true;
        });
    }

    public void CreateNewProfile() => 
        this.CreateProfile("Profile" + (this.profileByName.Count - 1).ToString(CultureInfo.InvariantCulture));

    public void CreateProfile(string name) => 
        this.CreateProfile(name, new Configuration());

    public void CreateProfile(string name, Configuration config)
    {
        DebugLog($"Creating new Profile: {name}");

        ProfileData profile = new()
                           {
                               Name   = name,
                               Config = config
                           };

        this.profileData.Add(profile);
        this.profileByName.Add(name, profile);
        this.SetProfile(name);
    }

    public void DuplicateCurrentProfile()
    {
        string name;
        int    counter = 0;

        string templateName = this.ActiveProfileName.EndsWith("_Copy") ? this.ActiveProfileName : $"{this.ActiveProfileName}_Copy";

        do
            name = counter++ > 0 ? $"{templateName}{counter}" : templateName;
        while (this.profileByName.ContainsKey(name));

        string?        oldConfig = EzConfig.DefaultSerializationFactory.Serialize(this.GetCurrentConfig);
        if(oldConfig != null)
        {
            Configuration? newConfig = EzConfig.DefaultSerializationFactory.Deserialize<Configuration>(oldConfig);
            if(newConfig != null)
                this.CreateProfile(name, newConfig);
        }
    }

    public void RemoveCurrentProfile()
    {
        DebugLog("Removing " + this.ActiveProfileName);
        this.profileData.Remove(this.GetCurrentProfile);
        this.profileByName.Remove(this.ActiveProfileName);
        this.SetProfileToDefault();
    }

    public bool RenameCurrentProfile(string newName)
    {
        if (this.profileByName.ContainsKey(newName))
            return false;

        ProfileData config = this.GetCurrentProfile;
        this.profileByName.Remove(this.ActiveProfileName);
        this.profileByName[newName] = config;
        config.Name                 = newName;
        this.activeProfileName      = newName;

        EzConfig.Save();

        return true;
    }

    public ProfileData? GetProfile(string name) => 
        this.profileByName.GetValueOrDefault(name);

    public void SetCharacterDefault()
    {
        Svc.Framework.RunOnTick(() =>
                          {

                              if (!PlayerHelper.IsValid)
                                  return;

                              ulong cid = Player.CID;

                              if (this.profileByCID.TryGetValue(cid, out string? oldProfile))
                                  this.profileByName[oldProfile].CIDs.Remove(cid);

                              this.GetCurrentProfile.CIDs.Add(cid);
                              this.profileByCID.Add(cid, this.ActiveProfileName);
                              this.charByCID[cid] = new CharData
                                                    {
                                                        CID  = cid,
                                                        Name = Player.Name,
                                                        World = Player.CurrentWorld
                              };

                              EzConfig.Save();
                          });
    }

    public void RemoveCharacterDefault()
    {
        Svc.Framework.RunOnTick(() =>
                                {
                                    if (!PlayerHelper.IsValid)
                                        return;

                                    ulong cid = Player.CID;

                                    this.profileByName[this.ActiveProfileName].CIDs.Remove(cid);
                                    this.profileByCID.Remove(cid);

                                    EzConfig.Save();
                                });
    }

    public static void DebugLog(string message)
    {
        Svc.Log.Debug($"Configuration Main: {message}");
    }

    public static JsonSerializerSettings JsonSerializerSettings = new()
                                                                   {
                                                                       Formatting           = Formatting.Indented,
                                                                       DefaultValueHandling = DefaultValueHandling.Include,
                                                                       Converters           = [new StringEnumConverter(new DefaultNamingStrategy())],
                                                                       
                                                                   };
}

[JsonObject(MemberSerialization.OptOut)]
public class ProfileData
{
    public required string         Name;
    public          HashSet<ulong> CIDs = [];
    public required Configuration  Config;
}

public class AutoDutySerializationFactory : DefaultSerializationFactory, ISerializationFactory
{
    public override string DefaultConfigFileName { get; } = "AutoDutyConfig.json";

    public new string Serialize(object config) => 
        base.Serialize(config);

    public override byte[] SerializeAsBin(object config) => 
        Encoding.UTF8.GetBytes(this.Serialize(config));
}



[Serializable]
public class Configuration
{
    //Meta
    public HashSet<string>                                    DoNotUpdatePathFiles = [];
    public Dictionary<uint, Dictionary<string, JobWithRole>?> PathSelectionsByPath = [];

    //LogOptions
    public bool AutoScroll = true;
    public LogEventLevel LogEventLevel = LogEventLevel.Debug;

    //General Options
    internal AutoDutyMode autoDutyModeEnum = AutoDutyMode.Looping;
    public AutoDutyMode AutoDutyModeEnum
    {
        get => this.autoDutyModeEnum;
        set
        {
            this.autoDutyModeEnum               = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected           = null;
            Plugin.LevelingModeEnum        = LevelingMode.None;
        }
    }


    public int LoopTimes = 1;
    internal DutyMode dutyModeEnum = DutyMode.Support;
    public DutyMode DutyModeEnum
    {
        get => this.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist => Plugin.PlaylistCurrentEntry?.DutyMode ?? this.dutyModeEnum,
            AutoDutyMode.Looping or _ => this.dutyModeEnum
        };
        set
        {
            this.dutyModeEnum = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected = null;
            Plugin.LevelingModeEnum = LevelingMode.None;
        }
    }


    
    public bool Unsynced                       = false;
    public bool HideUnavailableDuties          = false;
    public bool PreferTrustOverSupportLeveling = false;

    public bool ShowMainWindowOnStartup = false;

    
    #region OverlayConfig
    internal bool showOverlay = true;
    public bool ShowOverlay
    {
        get => showOverlay;
        set
        {
            showOverlay = value;
            if (Plugin.Overlay != null)
                Plugin.Overlay.IsOpen = value;
        }
    }
    internal bool hideOverlayWhenStopped = false;
    public bool HideOverlayWhenStopped
    {
        get => hideOverlayWhenStopped;
        set 
        {
            hideOverlayWhenStopped = value;
            if (Plugin.Overlay != null)
            {
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => Plugin.Overlay.IsOpen = !value || Plugin.States.HasFlag(PluginState.Looping) || Plugin.States.HasFlag(PluginState.Navigating), () => Plugin.Overlay != null);
            }
        }
    }
    internal bool lockOverlay = false;
    public bool LockOverlay
    {
        get => lockOverlay;
        set 
        {
            lockOverlay = value;
            if (value)
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => { if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove)) Plugin.Overlay.Flags |= ImGuiWindowFlags.NoMove; }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => { if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove)) Plugin.Overlay.Flags -= ImGuiWindowFlags.NoMove; }, () => Plugin.Overlay != null);
        }
    }
    internal bool overlayNoBG = false;
    public bool OverlayNoBG
    {
        get => overlayNoBG;
        set
        {
            overlayNoBG = value;
            if (value)
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () => { if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) Plugin.Overlay.Flags |= ImGuiWindowFlags.NoBackground; }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () => { if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) Plugin.Overlay.Flags -= ImGuiWindowFlags.NoBackground; }, () => Plugin.Overlay != null);
        }
    }

    public bool OverlayAnchorBottom           = false;
    public bool ShowDutyLoopText       = true;
    public bool ShowActionText         = true;
    public bool UseSliderInputs        = false;
    public bool OverrideOverlayButtons = true;
    public bool GotoButton             = true;
    public bool TurninButton           = true;
    public bool DesynthButton          = true;
    public bool ExtractButton          = true;
    public bool RepairButton           = true;
    public bool EquipButton            = true;
    public bool CofferButton           = true;
    public bool TTButton               = true;
    #endregion

    #region DutyConfig
    //Duty Config Options
    public bool           AutoExitDuty                  = true;
    public bool           OnlyExitWhenDutyDone          = false;
    public bool           AutoManageRotationPluginState = true;
    public RotationPlugin rotationPlugin                = RotationPlugin.All;

    #region Wrath
    public bool                                Wrath_AutoSetupJobs { get; set; } = true;
    public Wrath_IPCSubscriber.DPSRotationMode Wrath_TargetingTank    = Wrath_IPCSubscriber.DPSRotationMode.Highest_Max;
    public Wrath_IPCSubscriber.DPSRotationMode Wrath_TargetingNonTank = Wrath_IPCSubscriber.DPSRotationMode.Lowest_Current;
    #endregion

    #region RSR

    public RSR_IPCSubscriber.TargetHostileType RSR_TargetHostileType    = RSR_IPCSubscriber.TargetHostileType.AllTargetsCanAttack;
    public RSR_IPCSubscriber.TargetingType     RSR_TargetingTypeTank    = RSR_IPCSubscriber.TargetingType.HighMaxHP;
    public RSR_IPCSubscriber.TargetingType     RSR_TargetingTypeNonTank = RSR_IPCSubscriber.TargetingType.LowHP;
    #endregion



    internal bool autoManageBossModAISettings   = true;
    public bool AutoManageBossModAISettings
    {
        get => autoManageBossModAISettings;
        set
        {
            autoManageBossModAISettings = value;
            HideBossModAIConfig = !value;
        }
    }

    #region BossMod
    public bool HideBossModAIConfig           = false;
    public bool BM_UpdatePresetsAutomatically = true;


    internal bool maxDistanceToTargetRoleBased = true;
    public bool MaxDistanceToTargetRoleBased
    {
        get => maxDistanceToTargetRoleBased;
        set
        {
            maxDistanceToTargetRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("MaxDistanceToTargetRoleBasedBMRoleChecks", () => Plugin.BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetFloat    = 2.6f;
    public float MaxDistanceToTargetAoEFloat = 12;

    internal bool positionalRoleBased = true;
    public bool PositionalRoleBased
    {
        get => positionalRoleBased;
        set
        {
            positionalRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("PositionalRoleBasedBMRoleChecks", () => Plugin.BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetRoleMelee  = 2.6f;
    public float MaxDistanceToTargetRoleRanged = 10f;
    #endregion

    internal bool       positionalAvarice = true;
    public   Positional PositionalEnum    = Positional.Any;

    public bool       AutoManageVnavAlignCamera      = true;
    public bool       LootTreasure                   = true;
    public LootMethod LootMethodEnum                 = LootMethod.AutoDuty;
    public bool       LootBossTreasureOnly           = false;
    public int        TreasureCofferScanDistance     = 25;
    public bool       RebuildNavmeshOnStuck          = true;
    public byte       RebuildNavmeshAfterStuckXTimes = 5;
    public int        MinStuckTime                   = 500;

    public bool PathDrawEnabled   = false;
    public int  PathDrawStepCount = 5;

    public bool       OverridePartyValidation        = false;
    public bool       UsingAlternativeRotationPlugin = false;
    public bool       UsingAlternativeMovementPlugin = false;
    public bool       UsingAlternativeBossPlugin     = false;

    public bool        TreatUnsyncAsW2W = true;
    public JobWithRole W2WJobs          = JobWithRole.Tanks;

    public bool IsW2W(Job? job = null, bool? unsync = null)
    {
        job ??= PlayerHelper.GetJob();

        if (this.W2WJobs.HasJob(job.Value))
            return true;

        unsync ??= this.Unsynced && this.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

        return unsync.Value && this.TreatUnsyncAsW2W;
    }
    #endregion

    #region PreLoop
    public bool                                       EnablePreLoopActions     = true;
    public bool                                       ExecuteCommandsPreLoop   = false;
    public List<string>                               CustomCommandsPreLoop    = [];
    public bool                                       RetireMode               = false;
    public RetireLocation                             RetireLocationEnum       = RetireLocation.Inn;
    public List<Vector3>                              PersonalHomeEntrancePath = [];
    public List<Vector3>                              FCEstateEntrancePath     = [];
    public bool                                       AutoEquipRecommendedGear;
    public GearsetUpdateSource                        AutoEquipRecommendedGearSource;
    public bool                                       AutoEquipRecommendedGearGearsetterOldToInventory;
    public bool                                       AutoRepair              = false;
    public uint                                       AutoRepairPct           = 50;
    public bool                                       AutoRepairSelf          = false;
    public RepairNpcData?                             PreferredRepairNPC      = null;
    public bool                                       AutoConsume             = false;
    public bool                                       AutoConsumeIgnoreStatus = false;
    public int