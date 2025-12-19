using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using Serilog.Events;
using System.Numerics;
using System.Text.Json.Serialization;

namespace AutoDuty.Data
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Dalamud.Bindings.ImGui;
    using Dalamud.Game.ClientState.Objects.Types;
    using ECommons.ImGuiMethods;
    using FFXIVClientStructs.FFXIV.Client.Game.Object;
    using Helpers;
    using Lumina.Excel.Sheets;
    using Newtonsoft.Json;
    using ECommons.GameFunctions;

    public class Classes
    {
        public class LogMessage
        {
            public string Message { get; set; } = string.Empty;
            public LogEventLevel LogEventLevel { get; set; }
        }

        public class Message
        {
            public string Sender { get; set; } = string.Empty;
            public List<PathAction> Action { get; set; } = [];
        }

        public class Content
        {
            public uint              RowId                  { get; init; }
            public uint              Id                     { get; init; }
            public string?           Name                   { get; init; }
            public string?           EnglishName            { get; init; }
            public uint              TerritoryType          { get; init; }
            public uint              ExVersion              { get; init; }
            public byte              ClassJobLevelRequired  { get; init; }
            public uint              ItemLevelRequired      { get; init; }
            public uint              DawnRowId              { get; init; }
            public int               DawnIndex              { get; init; } = -1;
            public ushort            DawnIndicator          { get; set; }
            public uint              ContentFinderCondition { get; init; }
            public uint              ContentType            { get; set; }
            public uint              ContentMemberType      { get; set; }
            public int               TrustIndex             { get; init; } = -1;
            public bool              VariantContent         { get; set; } = false;
            public int               VVDIndex               { get; init; } = -1;
            public int               GCArmyIndex            { get; init; } = -1;
            public bool              GCArmyContent          => this.GCArmyIndex >= 0;
            public List<TrustMember> TrustMembers           { get; }      = [];
            public DutyMode          DutyModes              { get; set; } = DutyMode.None;
            public uint              UnlockQuest            { get; init; }
        }

        public class TrustMember
        {
            public byte            Index      { get; set; }
            public byte[]          MemberIds  { get; set; } = [];
            public TrustRole       Role       { get; set; }         // 0 = DPS, 1 = Healer, 2 = Tank, 3 = G'raha All Rounder
            public ClassJob?       Job        { get; set; } = null; //closest actual job that applies. G'raha gets Blackmage
            public string          Name       { get; set; } = string.Empty;
            public TrustMemberName MemberName { get; set; }

            public uint Level { get; set; }
            public uint LevelCap { get; set; }
            public uint LevelInit { get; set; }
            public bool LevelIsSet { get; set; }
            public uint UnlockQuest { get; init; }

            public bool Available => this.UnlockQuest <= 0 || QuestManager.IsQuestComplete(this.UnlockQuest);

            public void ResetLevel()
            {
                this.Level      = this.LevelInit;
                this.LevelIsSet = this.LevelInit == this.LevelCap;
            }

            public void SetLevel(uint level)
            {
                if (level >= this.LevelInit-1)
                {
                    this.LevelIsSet = true;
                    this.Level         = level;
                }
            }
        }

        public class PathFile
        {
            [JsonPropertyName("actions")]
            public List<PathAction> Actions { get; set; } = [];

            [JsonPropertyName("meta")]
            public PathFileMetaData Meta { get; set; } = new()
            {
                CreatedAt = Plugin.Version,
                Changelog = [],
                Notes = []
            };
        }

        public class PathAction
        {
            [JsonPropertyName("tag")]
            public ActionTag Tag { get; set; } = ActionTag.None;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("position")]
            public Vector3 Position { get; set; } = Vector3.Zero;

            [JsonPropertyName("arguments")]
            public List<string> Arguments { get; set; } = [];

            [JsonPropertyName("")]
            public List<PathActionCondition> Conditions { get; set; } = [];

            [JsonPropertyName("note")]
            public string Note { get; set; } = string.Empty;
        }

        #region PathActionConditions
        public abstract class PathActionCondition
        {
            public static readonly Dictionary<string, Func<object, object, bool>> operations = new()
                                                                                                   {
                                                                                                       { ">", (x,  y) => Convert.ToSingle(x) > Convert.ToSingle(y) },
                                                                                                       { ">=", (x, y) => Convert.ToSingle(x) >= Convert.ToSingle(y) },
                                                                                                       { "<", (x,  y) => Convert.ToSingle(x) < Convert.ToSingle(y) },
                                                                                                       { "<=", (x, y) => Convert.ToSingle(x) <= Convert.ToSingle(y) },
                                                                                                       { "==", (x, y) => x                   == y },
                                                                                                       { "!=", (x, y) => x                   != y }
                                                                                                   };

            public abstract bool IsFulfilled();
            public abstract void DrawConfig();
        }

        public class PathActionConditionNot(PathActionCondition condition) : PathActionCondition
        {

            public required PathActionCondition condition = condition;

            public override bool IsFulfilled() => !this.condition.IsFulfilled();

            public override void DrawConfig() => this.condition.DrawConfig();
        }

        public class PathActionConditionJob : PathActionCondition
        {
            public const ConditionType TYPE = ConditionType.None;

            public          JobWithRole job = JobWithRole.All;
            public override bool        IsFulfilled() => 
                this.job.HasJob(PlayerHelper.GetJob());

            public override void        DrawConfig()  => 
                JobWithRoleHelper.DrawCategory(JobWithRole.All, ref this.job);
        }

        public class PathActionConditionActionStatus : PathActionCondition
        {
            public ActionType type = ActionType.Action;
            public uint       id;
            public uint       statusCode;

            public override bool IsFulfilled()
            {
                unsafe
                {
                    return ActionManager.Instance()->GetActionStatus(this.type, this.id) == this.statusCode;
                }
            }

            public override void DrawConfig()
            {
                ImGuiEx.EnumCombo("Action Type", ref this.type);
                ImGui.SameLine();
                ImGui.InputUInt("Action ID", ref this.id);
                ImGui.SameLine();
                ImGui.InputUInt("Status Code", ref this.statusCode);
            }
        }

        public class PathActionConditionItemCount : PathActionCondition
        {
            public uint   itemId;
            public uint   quantity;
            public string operatorValue = operations.Keys.First();

            public override bool IsFulfilled()
            {
                if (!operations.TryGetValue(this.operatorValue, out Func<object, object, bool>? operationFunc))
                    return false;
                int itemCount = InventoryHelper.ItemCount(this.itemId);
                return operationFunc(itemCount, this.quantity);
            }

            public override void DrawConfig()
            {
                ImGuiEx.InputUint("itemId", ref this.itemId);
                ImGui.SameLine();
                ImGuiEx.Combo("Operation", ref this.operatorValue, operations.Keys);
                ImGui.SameLine();
                ImGuiEx.InputUint("Quantity", ref this.quantity);
            }
        }

        public class PathActionConditionObjectData : PathActionCondition
        {
            public uint               baseId;
            public ObjectDataProperty property;
            public int                value;


            public override bool IsFulfilled()
            {
                IGameObject? gameObject = null;
                if ((gameObject = ObjectHelper.GetObjectByDataId(this.baseId)) != null)
                {
                    unsafe
                    {
                        GameObject* csObj = gameObject.Struct();

                        return this.property switch
                        {
                            ObjectDataProperty.EventState => csObj->EventState          == (byte)this.value,
                            ObjectDataProperty.IsTargetable => csObj->GetIsTargetable() == (this.value != 0),
                            _ => false
                        };
                    }
                }
                return false;
            }

            public override void DrawConfig()
            {
                ImGuiEx.InputUint("BaseId", ref this.baseId);
                ImGui.SameLine();
                ImGuiEx.EnumCombo("Property", ref this.property);
                ImGui.SameLine();
                ImGui.InputInt("Value", ref this.value);
            }
        }

        public class PathActionConditionDistance : PathActionCondition
        {
            public DistanceLocationTypes origin = DistanceLocationTypes.Location;
            public uint                  originId;
            public Vector3               originLoc;


            public DistanceLocationTypes target = DistanceLocationTypes.Player;
            public uint                  targetId;
            public Vector3               targetLoc;

            public string operatorValue = operations.Keys.First();

            public float distance = 1f;

            public override bool IsFulfilled()
            {
                unsafe
                {
                    Vector3 originVec = this.origin switch
                    {
                        DistanceLocationTypes.Player => Player.GameObject->Position,
                        DistanceLocationTypes.Object => ObjectHelper.GetObjectByDataId(this.originId)?.Position ?? Vector3.PositiveInfinity,
                        DistanceLocationTypes.Location => this.originLoc,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    Vector3 targetVec = this.target switch
                    {
                        DistanceLocationTypes.Player => Player.GameObject->Position,
                        DistanceLocationTypes.Object => ObjectHelper.GetObjectByDataId(this.targetId)?.Position ?? Vector3.NegativeInfinity,
                        DistanceLocationTypes.Location => this.targetLoc,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    float offset = Vector3.Distance(originVec, targetVec);

                    return operations.TryGetValue(this.operatorValue, out Func<object, object, bool>? operationFunc) && 
                           operationFunc(offset, this.distance);
                }
            }

            public override void DrawConfig()
            {
                ImGuiEx.EnumCombo("origin", ref this.origin);
                
                switch (this.origin)
                {
                    case DistanceLocationTypes.Object:
                        ImGui.SameLine();
                        ImGuiEx.InputUint("origin_baseID", ref this.originId);
                        break;
                    case DistanceLocationTypes.Location:
                        ImGui.SameLine();
                        ImGui.PushItemWidth(50f);
                        float x = this.originLoc.X;
                        ImGui.InputFloat("origin_X", ref x);
                        ImGui.SameLine();
                        float y = this.originLoc.Y;
                        ImGui.InputFloat("origin_Y", ref y);
                        ImGui.SameLine();
                        float z = this.originLoc.Z;
                        ImGui.InputFloat("origin_Z", ref z);
                        this.originLoc = new Vector3(x, y, z);
                        ImGui.PopItemWidth();
                        break;
                    case DistanceLocationTypes.Player:
                    default:
                        break;
                }
                

                ImGuiEx.EnumCombo("target", ref this.target);
                
                switch (this.target)
                {
                    case DistanceLocationTypes.Object:
                        ImGui.SameLine();
                        ImGuiEx.InputUint("target_baseID", ref this.targetId);
                        break;
                    case DistanceLocationTypes.Location:
                        ImGui.PushItemWidth(50f);
                        ImGui.SameLine();
                        float x = this.targetLoc.X;
                        ImGui.InputFloat("target_X", ref x);
                        ImGui.SameLine();
                        float y = this.targetLoc.Y;
                        ImGui.InputFloat("target_Y", ref y);
                        ImGui.SameLine();
                        float z = this.targetLoc.Z;
                        ImGui.InputFloat("target_Z", ref z);
                        this.targetLoc = new Vector3(x, y, z);
                        ImGui.PopItemWidth();
                        break;
                    case DistanceLocationTypes.Player:
                    default:
                        break;
                }
                
                ImGuiEx.Combo("Operation", ref this.operatorValue, operations.Keys);
                ImGui.SameLine();
                ImGui.InputFloat("Distance", ref this.distance);
            }
        }
    #endregion

        public class PathFileMetaData
        {
            [JsonPropertyName("createdAt")]
            public int CreatedAt { get; set; }

            [JsonPropertyName("changelog")]
            public List<PathFileChangelogEntry> Changelog { get; set; } = [];

            [JsonIgnore]
            public int LastUpdatedVersion => this.Changelog.Count > 0 ? this.Changelog.Last().Version : this.CreatedAt;

            [JsonPropertyName("notes")]
            public List<string> Notes { get; set; } = [];
        }

        public class PathFileChangelogEntry
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("change")]
            public string Change { get; set; } = string.Empty;
        }

        public class PollResponseClass
        {
            [JsonPropertyName("interval")]
            public int Interval { get; set; } = -1;

            [JsonPropertyName("error")]
            public string Error { get; set; } = string.Empty;

            [JsonPropertyName("error_description")]
            public string Error_Description { get; set; } = string.Empty;

            [JsonPropertyName("error_uri")]
            public string Error_Uri { get; set; } = string.Empty;

            [JsonPropertyName("access_token")]
            public string Access_Token = string.Empty;

            [JsonPropertyName("expires_in")]
            public int Expires_In { get; set; } = 0;

            [JsonPropertyName("refresh_token")]
            public string Refresh_Token = string.Empty;

            [JsonPropertyName("refresh_token_expires_in")]
            public int Refresh_Token_Expires_In = 0;

            [JsonPropertyName("token_type")]
            public string Token_Type = string.Empty;

            [JsonPropertyName("scope")]
            public string Scope = string.Empty;
        }

        public class UserCode
        {
            [JsonPropertyName("device_code")]
            public string Device_Code { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int Expires_In { get; set; } = 0;

            [JsonPropertyName("user_code")]
            public string User_Code { get; set; } = string.Empty;

            [JsonPropertyName("verification_uri")]
            public string Verification_Uri { get; set; } = string.Empty;

            [JsonPropertyName("interval")]
            public int Interval { get; set; } = 500;
        }

        public class GitHubIssue
        {
            [JsonPropertyName("title")]
            public string Title { get; set; } = "[Bug] ";

            [JsonPropertyName("body")]
            public string Body { get; set; } = string.Empty;

            [JsonPropertyName("labels")]
            public List<string> Labels = ["bug", "unconfirmed"];

            public static string Version => $"{Plugin.Version}";

            public static string LogFile => Plugin.dalamudLogEntries.SelectMulti(x => x.Message).ToList().ToCustomString("\n");

            public static string InstalledPlugins => PluginInterface.InstalledPlugins.Select(x => $"{x.InternalName}, Version= {x.Version}").ToList().ToCustomString("\n");

            public static string ConfigFile => ReadConfigFile().ToCustomString("\n");

            private static List<string> ReadConfigFile()
            {
                using FileStream fs = new(Plugin.configFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader sr = new(fs);
                string? x;
                List<string> strings = [];
                while ((x = sr.ReadLine()) != null) strings.Add(x);
                return strings;
            }
        }

        [JsonObject(MemberSerialization.OptOut)]
        public class PlaylistEntry
        {
            private uint id = 0;

            public uint Id
            {
                get => this.id;
                set
                {
                    if (value != this.id)
                    {
                        this.path    = ContentPathsManager.DictionaryPaths[value].SelectPath(out _)!.FileName;
                        this.content = null;
                    }

                    this.id = value;
                }
            }

            [JsonIgnore]
            private Content? content;

            public Content? Content => 
                this.content ??= this.id == 0 ? null : ContentHelper.DictionaryContent[this.id];

            private DutyMode dutyMode;

            public DutyMode DutyMode
            {
                get => this.dutyMode;
                set
                {
                    if (value != this.dutyMode && !(this.Content?.DutyModes.HasFlag(value) ?? false))
                        this.Id = ContentPathsManager.DictionaryPaths.Keys.FirstOrDefault(key => ContentHelper.DictionaryContent[key].DutyModes.HasFlag(value));
                    this.dutyMode = value;
                }
            }

            public string path = string.Empty;

            public int count    = 1;
            public int curCount = 0;

            public byte? gearset;

            public bool unsynced;
        }
    }
}
