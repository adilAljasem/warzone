namespace Warzone;

public static class BotNames
{
    // 220 unique realistic mixed Arabic & English player names (110 Arabic + 110 English)
    // so 15+ simultaneous live matches never reuse the same player name across active rooms.
    public static readonly string[] Pool =
    [
        // 110 Arabic player names
        "أبو فهد",
        "صقر العرب",
        "فارس الصحراء",
        "ذيب الشمال",
        "سلطان الحرب",
        "النمر الأسود",
        "أبو تركي",
        "عمر القائد",
        "خالد السيف",
        "فيصل البطل",
        "يوسف الشجاع",
        "الجنرال طارق",
        "أسد الجزيرة",
        "سالم الفارس",
        "بدر الليل",
        "نواف الساحق",
        "راكان",
        "المنتصر",
        "صلاح الدين",
        "حسام",
        "أبو عزام",
        "المهاجر",
        "عزوز",
        "حمودي",
        "سعود",
        "عبدالله",
        "ماجد",
        "وليد",
        "تركي",
        "باسل",
        "القيصر",
        "زيد البطل",
        "أمير الحرب",
        "شبح الليل",
        "راشد",
        "مشعل",
        "عادل",
        "يزيد",
        "مهند",
        "لؤي",
        "أبو خالد",
        "الرمح الذهبي",
        "سيف النصر",
        "قاهر الأعداء",
        "الذئب الرمادي",
        "نادر",
        "أحمد",
        "كريم",
        "مصطفى",
        "إبراهيم",
        "أبو سارة",
        "عقرب الصحراء",
        "فهد النمر",
        "أبو نايف",
        "حمزة",
        "ريان",
        "أنس",
        "عبدالرحمن",
        "محمد القائد",
        "سلطان نجد",
        "ضاري",
        "سطام",
        "متعب",
        "بندر",
        "ناصر",
        "فارس الليل",
        "صقر الجزيرة",
        "عاصفة الرمل",
        "برق الشمال",
        "رعد الجنوب",
        "أبو ريان",
        "عمران",
        "ياسر",
        "مازن",
        "تيمور",
        "جواد",
        "شادي",
        "سامي",
        "مروان",
        "أشرف",
        "أيمن",
        "هيثم",
        "زياد",
        "طلال",
        "غسان",
        "عدنان",
        "بلال",
        "أسامة",
        "حذيفة",
        "معاذ",
        "قيس",
        "ليث",
        "غيث",
        "وسيم",
        "رامي",
        "هشام",
        "فادي",
        "أبو يوسف",
        "السفاح العربي",
        "قائد الفيلق",
        "حاكم الوادي",
        "ملك القلعة",
        "ذئب الليل",
        "أسد الشرق",
        "بطل المعركة",
        "أبو راشد",
        "نمر الصحراء",
        "سيف العرب",
        "الشجاع",
        "صائد الملوك",

        // 110 English player names
        "ShadowKnight",
        "IronWolf",
        "DarkViper",
        "StormBreaker",
        "NightHawk",
        "RedBaron",
        "GhostRider",
        "SilverBlade",
        "FrostKing",
        "DragonSlayer",
        "ApexHunter",
        "SteelTitan",
        "Valkyrie",
        "BlazeRunner",
        "WolfPack",
        "Kratos99",
        "NovaStrike",
        "CrimsonFury",
        "ZeroCool",
        "SilentArrow",
        "KingArthur",
        "SpartanX",
        "Caesar_IV",
        "Maximus",
        "RavenClaw",
        "ThunderBolt",
        "Vortex",
        "TitanFall",
        "Obsidian",
        "FalconEye",
        "CyberSamurai",
        "WarLord77",
        "PhoenixRise",
        "RogueOne",
        "VenomX",
        "Ares_War",
        "Gladiator",
        "SniperWolf",
        "OmegaKing",
        "Maverick",
        "KnightMare",
        "StormChaser",
        "GoldenEagle",
        "DesertFox",
        "SunTzu",
        "Conqueror",
        "Eclipse9",
        "CobraStrike",
        "IronFist",
        "LegendX",
        "BlackPanther",
        "RazorEdge",
        "FrostBite",
        "BloodHawk",
        "SkyWalker",
        "DarkPaladin",
        "WildFang",
        "DreadLord",
        "SolarFlare",
        "ShadowFox",
        "VikingKing",
        "ReaperX",
        "SteelHawk",
        "BladeMaster",
        "FlameKnight",
        "ThunderKing",
        "Raptor99",
        "GhostWolf",
        "Vanguard",
        "WarHammer",
        "NightStalker",
        "StormLord",
        "AceCommander",
        "KingSlayer",
        "IronClad",
        "SilverWolf",
        "RedViper",
        "ChaosKnight",
        "PhantomX",
        "AtlasStrong",
        "Leonidas",
        "Achilles",
        "Hannibal",
        "SaladinX",
        "Templar",
        "Centurion",
        "NomadKing",
        "KhanRider",
        "DesertHawk",
        "SandStorm",
        "Hyperion",
        "Zephyr",
        "Spectre07",
        "Tactician",
        "MarshalX",
        "Crusader",
        "Sentinel",
        "Valkor",
        "Drakon",
        "Strider",
        "BaneOfKings",
        "NorthStar",
        "FireStorm",
        "TitanCore",
        "GrimReaper",
        "Cobalt",
        "NexusPro",
        "Ragnarok",
        "ViperStrike",
        "AegisGuard"
    ];

    // Realistic mixed Arabic & English realm names for live & matchmaking rooms.
    public static readonly string[] RoomNames =
    [
        "حرب الممالك",
        "ساحة الأبطال",
        "وادي الملوك",
        "معركة التاج",
        "أرض الفرسان",
        "صراع الحضارات",
        "مملكة الشمس",
        "تحدي القادة",
        "عرش الوادي",
        "ملحمة الصحراء",
        "قلعة النصر",
        "فرسان الشرق",
        "عاصفة الحرب",
        "سادة الممالك",
        "نصر القادة",
        "Sunmeadow Clash",
        "Crown Battle",
        "Kings Arena",
        "Valley Conquest",
        "Warlords Realm",
        "Imperial Siege",
        "Golden Frontier",
        "Grand Dominion",
        "Desert Empires",
        "Iron Throne",
        "Battle of Titans",
        "Middle East Arena",
        "Royal Showdown",
        "Clash of Kingdoms",
        "Legends Valley"
    ];

    public static string PickPlayerName(HashSet<string> usedNames)
    {
        var start = Random.Shared.Next(Pool.Length);
        for (var i = 0; i < Pool.Length; i++)
        {
            var candidate = Pool[(start + i) % Pool.Length];
            if (usedNames.Add(candidate))
                return candidate;
        }
        // Fallback if > 220 simultaneous bots exist (e.g. admin 50-room stress test)
        var baseName = Pool[Random.Shared.Next(Pool.Length)];
        for (var n = 2; n < 9999; n++)
        {
            var candidate = $"{baseName}_{n}";
            if (candidate.Length <= 32 && usedNames.Add(candidate))
                return candidate;
        }
        var fallback = $"Player_{Random.Shared.Next(1000, 9999)}";
        usedNames.Add(fallback);
        return fallback;
    }

    public static string PickRoomName(HashSet<string> usedRoomNames)
    {
        var start = Random.Shared.Next(RoomNames.Length);
        for (var i = 0; i < RoomNames.Length; i++)
        {
            var candidate = RoomNames[(start + i) % RoomNames.Length];
            if (usedRoomNames.Add(candidate))
                return candidate;
        }
        var baseName = RoomNames[Random.Shared.Next(RoomNames.Length)];
        for (var n = 2; n < 200; n++)
        {
            var numbered = $"{baseName} #{n}";
            if (usedRoomNames.Add(numbered))
                return numbered;
        }
        return $"{baseName} #{Random.Shared.Next(100, 999)}";
    }
}
