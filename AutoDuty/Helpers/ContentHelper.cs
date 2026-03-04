using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Data;

namespace AutoDuty.Helpers
{
    using System.Collections.Generic;
    using System.Linq;
    using static Data.Classes;
    using Dalamud.Utility;
    using Lumina.Excel;
    using Lumina.Excel.Sheets;

    internal static class ContentHelper
    {
        internal static Dictionary<uint, Content> DictionaryContent { get; set; } = [];
        
        private static List<uint> ListGCArmyContent { get; set; } = [1245, 1039, 1041, 1042, 1330, 1331, 0/*159*/, 160, 349, 362, 188, 1064, 1066, 430, 510]; //Wanderer's Palace out for now.
        
        private static List<uint> ListVVDContent { get; set; } = [1069, 1137, 1176]; //[1069, 1075, 1076, 1137, 1155, 1156, 1176, 1179, 1180]; *Criterions

        private static bool TryGetDawnIndex(uint indexIn, uint ex, out int indexOut)
        {
            indexOut = -1;
            if (indexIn < 1) 
                return false;
            indexOut = DawnIndex(indexIn, ex);
            return true;
        }

        private static int DawnIndex(uint index, uint ex) =>
            ex switch
            {
                0 => (int)index - 200,
                1 => (int)index - 215,
                2 => (int)index - 224,
                3 => (int)index - 1,
                4 => (int)index - 12,
                5 => (int)index - 24,
                _ => -1
            };

        private static bool TryGetTrustIndex(int indexIn, uint ex, out int indexOut)
        {
            indexOut = -1;
            if (indexIn < 0) return false;
            indexOut = TrustIndex(indexIn, ex);
            return true;
        }

        private static int TrustIndex(int index, uint ex) =>
            ex switch
            {
                3 => index,
                4 => index - 11,
                5 => index - 22,
                _ => -1
            };

        internal static void PopulateDuties()
        {
            ExcelSheet<ContentFinderCondition>? listContentFinderCondition     = Svc.Data.GameData.GetExcelSheet<ContentFinderCondition>();
            ExcelSheet<ContentFinderCondition>? contentFinderConditionsEnglish = Svc.Data.GameData.GetExcelSheet<ContentFinderCondition>(Language.English);

            ExcelSheet<DawnContent>? listDawnContent = Svc.Data.GameData.GetExcelSheet<DawnContent>();

            SubrowExcelSheet<DawnContentParticipable>? listDawnParticipableContent = Svc.Data.GameData.GetSubrowExcelSheet<DawnContentParticipable>();


            if (listContentFinderCondition == null || listDawnContent == null || listDawnParticipableContent == null) return;

            foreach (ContentFinderCondition contentFinderCondition in listContentFinderCondition)
            {


                if (contentFinderCondition.ContentType.ValueNullable == null || contentFinderCondition.TerritoryType.ValueNullable?.ExVersion.ValueNullable == null || contentFinderCondition.ContentType.Value.RowId is not (2 or 4 or 5 or 20 or 30) || contentFinderCondition.Name.ToString().IsNullOrEmpty())
                    continue;

                static string CleanName(string name)
                {
                    string result = char.ToUpper(name.First()) + name[1..];
                    return result;
                }
                
                DawnContent?           dawnContent      = listDawnContent.FirstOrDefault(x => x.Content.ValueNullable?.RowId == contentFinderCondition.RowId);
                ContentFinderCondition englishCondition = contentFinderConditionsEnglish?.GetRow(contentFinderCondition.RowId) ?? contentFinderCondition;

                Content content = new()
                                  {
                                      UnlockQuest            = dawnContent?.RowId != default(uint) ? dawnContent?.Unknown0 ?? 0 : 0,
                                      Id                     = contentFinderCondition.Content.RowId,
                                      RowId                  = contentFinderCondition.RowId,
                                      Name                   = CleanName(contentFinderCondition.Name.ToDalamudString().TextValue),
                                      EnglishName            = CleanName(englishCondition!.Name.ToDalamudString().TextValue),
                                      TerritoryType          = contentFinderCondition.TerritoryType.Value.RowId,
                                      ContentType            = contentFinderCondition.ContentType.Value.RowId,
                                      ContentMemberType      = contentFinderCondition.ContentMemberType.ValueNullable?.RowId ?? 0,
                                      ContentFinderCondition = contentFinderCondition.RowId,
                                      ExVersion              = contentFinderCondition.TerritoryType.Value.ExVersion.Value.RowId,
                                      ClassJobLevelRequired  = contentFinderCondition.ClassJobLevelRequired,
                                      ItemLevelRequired      = contentFinderCondition.ItemLevelRequired,
                                      DawnRowId              = dawnContent?.RowId ?? 0,
                                      DawnIndex              = TryGetDawnIndex(dawnContent?.RowId ?? 0, contentFinderCondition.TerritoryType.Value.ExVersion.Value.RowId, out int dawnIndex) ? dawnIndex : -1,
                                      DawnIndicator          = (ushort)(dawnContent?.RowId != default(uint) ? dawnContent?.Unknown15 ?? 0u : 0u),
                                      TrustIndex = TryGetTrustIndex(listDawnContent.Where(dawnC => dawnC.Unknown13).IndexOf(x => x.Content.Value.RowId == contentFinderCondition.RowId),
                                                                    contentFinderCondition.TerritoryType.Value.ExVersion.Value.RowId, out int trustIndex) ? trustIndex : -1,
                                      VariantContent = ListVVDContent.Any(variantContent => variantContent        == contentFinderCondition.TerritoryType.Value.RowId),
                                      VVDIndex       = ListVVDContent.FindIndex(variantContent => variantContent  == contentFinderCondition.TerritoryType.Value.RowId),
                                      GCArmyIndex    = ListGCArmyContent.FindIndex(gcArmyContent => gcArmyContent == contentFinderCondition.TerritoryType.Value.RowId)
                                  };

                switch (contentFinderCondition.ContentType.Value.RowId)
                {
                    case 2:
                        content.DutyModes |= DutyMode.Regular;
                        break;
                    case 4:
                        content.DutyModes |= DutyMode.Trial;
                        break;
                    case 5:
                        content.DutyModes |= DutyMode.Raid;
                        break;
                    case 20:
                        content.DutyModes |= DutyMode.NoviceHall;
                        break;
                    case 30 when contentFinderCondition.TerritoryType.Value.RowId.EqualsAny(ListVVDContent):
                        content.DutyModes |= DutyMode.Variant;
                        break;
                }

                if (contentFinderCondition.TerritoryType.Value.RowId.EqualsAny(ListGCArmyContent))
                    content.DutyModes |= DutyMode.Squadron;

                if (content.DawnIndex > -1 && listDawnParticipableContent.GetSubrowCount(dawnContent!.Value.RowId) > 1)
                    content.DutyModes |= DutyMode.Support;

                if (content.TrustIndex > -1)
                    content.DutyModes |= DutyMode.Trust;

                if (content.DutyModes.HasFlag(DutyMode.Trust))
                {
                    content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Alphinaud]);
                    content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Alisaie]);
                    content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Thancred]);
                    content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Urianger]);
                    content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Yshtola]);
                    content.TrustMembers.Add(TrustHelper.Members[content.ExVersion == 3 ?
                                                                      TrustMemberName.Ryne :
                                                                      TrustMemberName.Estinien
                                                                 ]);

                    content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Graha]);
                    if (content.TerritoryType is >= 1097 and <= 1164)
                        content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Zero]);
                    if (content.ExVersion == 5)
                        content.TrustMembers.Add(TrustHelper.Members[TrustMemberName.Krile]);
                }

                DictionaryContent.Add(contentFinderCondition.TerritoryType.Value.RowId, content);
            }
            
            DictionaryContent = DictionaryContent.OrderBy(content => content.Value.ExVersion).
                                                  ThenBy(content => content.Value.ClassJobLevelRequired).
                                                  ThenBy(content => content.Value.ItemLevelRequired).
                                                  ThenBy(content => content.Value.TerritoryType).ToDictionary();
        }

        public static bool CanRun(this Content content, short level = -1, DutyMode mode = DutyMode.None, bool trustCheckLevels = true, bool? unsync = null, uint? ilvl = null)
        {
            if ((Player.Available ? Player.Job.GetCombatRole() : CombatRole.NonCombat) == CombatRole.NonCombat)
                return false;

            if (!UIState.IsInstanceContentUnlocked(content.Id))
                return false;

            if (level < 0) 
                level = PlayerHelper.GetCurrentLevelFromSheet();

            if (mode == DutyMode.None)
                mode = Configuration.DutyModeEnum;

            unsync ??= Configuration.Unsynced && mode.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

            if (unsync.Value)
                if (content.ExVersion == 5)
                    return false;

            if (content.ClassJobLevelRequired > level ||
                !ContentPathsManager.DictionaryPaths.ContainsKey(content.TerritoryType))
                return false;
            
            ilvl ??= InventoryHelper.CurrentItemLevel;
            if (!unsync.Value && content.ItemLevelRequired > ilvl)
                return false;

            if (mode.HasFlag(DutyMode.Trust))
                if (!content.CanTrustRun(trustCheckLevels))
                    return false;

            if(mode.HasFlag(DutyMode.NoviceHall))
                if (!content.CanRunNovice())
                    return false;

            return true;

        }
    }
}
