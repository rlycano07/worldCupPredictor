using System.Globalization;
using System.Text;
using WorldCupPredict.Models;

namespace WorldCupPredict.Services;

public static class TournamentData
{
    public static IReadOnlyList<Group> Groups { get; } =
    [
        CreateGroup("A", "Group A", "Mexico", "South Africa", "Korea Republic", "Czechia"),
        CreateGroup("B", "Group B", "Canada", "Bosnia and Herzegovina", "Qatar", "Switzerland"),
        CreateGroup("C", "Group C", "Brazil", "Morocco", "Haiti", "Scotland"),
        CreateGroup("D", "Group D", "United States", "Paraguay", "Australia", "Türkiye"),
        CreateGroup("E", "Group E", "Germany", "Curaçao", "Côte d'Ivoire", "Ecuador"),
        CreateGroup("F", "Group F", "Netherlands", "Japan", "Sweden", "Tunisia"),
        CreateGroup("G", "Group G", "Belgium", "Egypt", "Iran", "New Zealand"),
        CreateGroup("H", "Group H", "Spain", "Cabo Verde", "Saudi Arabia", "Uruguay"),
        CreateGroup("I", "Group I", "France", "Senegal", "Iraq", "Norway"),
        CreateGroup("J", "Group J", "Argentina", "Algeria", "Austria", "Jordan"),
        CreateGroup("K", "Group K", "Portugal", "Congo DR", "Uzbekistan", "Colombia"),
        CreateGroup("L", "Group L", "England", "Croatia", "Ghana", "Panama")
    ];

    public static IReadOnlyList<KnockoutMapping> RoundOf32Mapping { get; } =
    [
        Map(73, "A", 2, "B", 2),
        MapBestThird(74, "E", 1, "A/B/C/D/F"),
        Map(75, "F", 1, "C", 2),
        Map(76, "C", 1, "F", 2),
        MapBestThird(77, "I", 1, "C/D/F/G/H"),
        Map(78, "E", 2, "I", 2),
        MapBestThird(79, "A", 1, "C/E/F/H/I"),
        MapBestThird(80, "L", 1, "E/H/I/J/K"),
        MapBestThird(81, "D", 1, "B/E/F/I/J"),
        MapBestThird(82, "G", 1, "A/E/H/I/J"),
        Map(83, "K", 2, "L", 2),
        Map(84, "H", 1, "J", 2),
        MapBestThird(85, "B", 1, "E/F/G/I/J"),
        Map(86, "J", 1, "H", 2),
        MapBestThird(87, "K", 1, "D/E/I/J/L"),
        Map(88, "D", 2, "G", 2)
    ];

    public static IReadOnlyList<string> BestThirdPlaceGroupPriority { get; } =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L"
    ];

    public static Team? FindTeam(string teamId) =>
        Groups
            .SelectMany(group => group.Teams)
            .FirstOrDefault(team => team.Id == teamId);

    private static Group CreateGroup(string id, string name, params string[] teamNames) =>
        new()
        {
            Id = id,
            Name = name,
            Teams = teamNames
                .Select(teamName =>
                {
                    var flagCode = FlagCode(teamName);
                    return new Team(CreateTeamId(teamName), teamName, flagCode, FlagImageUrl(flagCode));
                })
                .ToList()
        };

    private static KnockoutMapping Map(
        int matchNumber,
        string groupA,
        int positionA,
        string groupB,
        int positionB
    ) =>
        new(
            $"r32-{matchNumber}",
            new KnockoutSlot(groupA, positionA),
            new KnockoutSlot(groupB, positionB)
        );

    private static KnockoutMapping MapBestThird(
        int matchNumber,
        string groupA,
        int positionA,
        string eligibleThirdPlaceGroups
    ) =>
        new(
            $"r32-{matchNumber}",
            new KnockoutSlot(groupA, positionA),
            new KnockoutSlot($"Best 3rd {eligibleThirdPlaceGroups}", 3)
        );

    private static string CreateTeamId(string name)
    {
        string normalized = name.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (char character in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace(".", "");
    }

    private static string FlagCode(string name) => name switch
    {
        "Algeria" => "ALG",
        "Argentina" => "ARG",
        "Australia" => "AUS",
        "Austria" => "AUT",
        "Belgium" => "BEL",
        "Bosnia and Herzegovina" => "BIH",
        "Brazil" => "BRA",
        "Cabo Verde" => "CPV",
        "Canada" => "CAN",
        "Colombia" => "COL",
        "Congo DR" => "COD",
        "Croatia" => "CRO",
        "Curaçao" => "CUW",
        "Czechia" => "CZE",
        "Côte d'Ivoire" => "CIV",
        "Ecuador" => "ECU",
        "Egypt" => "EGY",
        "England" => "ENG",
        "France" => "FRA",
        "Germany" => "GER",
        "Ghana" => "GHA",
        "Haiti" => "HAI",
        "Iran" => "IRN",
        "Iraq" => "IRQ",
        "Japan" => "JPN",
        "Jordan" => "JOR",
        "Korea Republic" => "KOR",
        "Mexico" => "MEX",
        "Morocco" => "MAR",
        "Netherlands" => "NED",
        "New Zealand" => "NZL",
        "Norway" => "NOR",
        "Panama" => "PAN",
        "Paraguay" => "PAR",
        "Portugal" => "POR",
        "Qatar" => "QAT",
        "Saudi Arabia" => "KSA",
        "Scotland" => "SCO",
        "Senegal" => "SEN",
        "South Africa" => "RSA",
        "Spain" => "ESP",
        "Sweden" => "SWE",
        "Switzerland" => "SUI",
        "Tunisia" => "TUN",
        "Türkiye" => "TUR",
        "United States" => "USA",
        "Uruguay" => "URU",
        "Uzbekistan" => "UZB",
        _ => "TBD"
    };

    private static string FlagImageUrl(string flagCode)
    {
        var slug = FlagImageSlug(flagCode);
        return string.IsNullOrWhiteSpace(slug) ? string.Empty : $"https://flagcdn.com/{slug}.svg";
    }

    private static string FlagImageSlug(string flagCode) => flagCode switch
    {
        "ALG" => "dz",
        "ARG" => "ar",
        "AUS" => "au",
        "AUT" => "at",
        "BEL" => "be",
        "BIH" => "ba",
        "BRA" => "br",
        "CAN" => "ca",
        "CIV" => "ci",
        "COD" => "cd",
        "COL" => "co",
        "CPV" => "cv",
        "CRO" => "hr",
        "CUW" => "cw",
        "CZE" => "cz",
        "ECU" => "ec",
        "EGY" => "eg",
        "ENG" => "gb-eng",
        "FRA" => "fr",
        "GER" => "de",
        "GHA" => "gh",
        "HAI" => "ht",
        "IRN" => "ir",
        "IRQ" => "iq",
        "JOR" => "jo",
        "JPN" => "jp",
        "KOR" => "kr",
        "KSA" => "sa",
        "MAR" => "ma",
        "MEX" => "mx",
        "NED" => "nl",
        "NOR" => "no",
        "NZL" => "nz",
        "PAN" => "pa",
        "PAR" => "py",
        "POR" => "pt",
        "QAT" => "qa",
        "RSA" => "za",
        "SCO" => "gb-sct",
        "SEN" => "sn",
        "ESP" => "es",
        "SUI" => "ch",
        "SWE" => "se",
        "TUN" => "tn",
        "TUR" => "tr",
        "URU" => "uy",
        "USA" => "us",
        "UZB" => "uz",
        _ => string.Empty
    };
}
