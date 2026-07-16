using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace JETHelper.Windows;

/// <summary>
/// Displays the projects, data sources, and contributors acknowledged by
/// JETHelper. The structure mirrors the repository's ACKNOWLEDGEMENTS.md file.
/// </summary>
public sealed class AcknowledgementsWindow : Window, IDisposable {
    private const string EdrdgWebsiteUrl = "https://www.edrdg.org/";
    private const string EdrdgLicenceUrl
              = "https://www.edrdg.org/edrdg/licence.html";
    private const string JmdictProjectUrl
              = "https://www.edrdg.org/wiki/index.php/"
                + "JMdict-EDICT_Dictionary_Project";
    private const string KanjidicProjectUrl
              = "https://www.edrdg.org/wiki/index.php/KANJIDIC_Project";
    private const string YomitanConversionUrl
              = "https://github.com/yomidevs/jmdict-yomitan";
    private const string JitenUrl = "https://jiten.moe/";
    private const string JitenFrequencyUrl = "https://jiten.moe/other";
    private const string CreativeCommonsBySaUrl
              = "https://creativecommons.org/licenses/by-sa/4.0/";
    private const string MarvDictionaryRepositoryUrl
              = "https://github.com/MarvNC/yomitan-dictionaries";
    private const string MarvDictionaryFolderUrl
              = "https://drive.google.com/drive/folders/"
                + "1LXMIOoaWASIntlx1w08njNU005lS5lez";
    private const string YomitanUrl = "https://github.com/themoeway/yomitan";
    private const string AnkiUrl = "https://apps.ankiweb.net/";
    private const string AnkiConnectUrl
              = "https://github.com/FooSoft/anki-connect";
    private const string DalamudUrl = "https://github.com/goatcorp/Dalamud";
    private const string JetHelperRepositoryUrl
              = "https://github.com/J-Lehrack/JETHelper";

    public AcknowledgementsWindow() :
          base("JETHelper Acknowledgements###JETHelperAcknowledgements")
    {
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(560, 440),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
        // No unmanaged resources yet.
    }

    public override void Draw()
    {
        ImGui.TextWrapped(
                  "JETHelper is built with help from open-source projects, "
                  + "community-maintained resources, and dictionary data made "
                  + "available for Japanese-language study.");
        ImGui.Spacing();

        DrawBundledDictionarySection();
        DrawSectionSeparator();
        DrawUpdateSection();
        DrawSectionSeparator();
        DrawCommunityDictionarySection();
        DrawSectionSeparator();
        DrawRelatedProjectsSection();
        DrawSectionSeparator();
        DrawContributorsSection();
        DrawSectionSeparator();
        DrawJetHelperSection();
    }

    private static void DrawBundledDictionarySection()
    {
        ImGui.TextUnformatted("Bundled dictionaries");
        ImGui.BulletText("JMdict (English) — vocabulary and reading data");
        ImGui.BulletText(
                  "KANJIDIC/KANJIDIC2 (English) — kanji meanings and readings");
        ImGui.BulletText("Jiten Frequency Global — global Japanese "
                         + "word-frequency ranks");
        ImGui.Spacing();

        ImGui.TextWrapped(
                  "JMdict and KANJIDIC/KANJIDIC2 are maintained by the "
                  + "Electronic Dictionary Research and Development Group "
                  + "(EDRDG). Their bundled Yomitan-compatible archives are "
                  + "obtained from the community-maintained "
                  + "yomidevs/jmdict-yomitan releases.");
        ImGui.Spacing();

        DrawLinkButton("EDRDG website", EdrdgWebsiteUrl);
        ImGui.SameLine();
        DrawLinkButton("EDRDG licence", EdrdgLicenceUrl);

        DrawLinkButton("JMdict project", JmdictProjectUrl);
        ImGui.SameLine();
        DrawLinkButton("KANJIDIC project", KanjidicProjectUrl);

        DrawLinkButton("JMdict/KANJIDIC releases", YomitanConversionUrl);

        ImGui.Spacing();
        ImGui.TextWrapped(
                  "Jiten Frequency Global is created and maintained by the "
                  + "Jiten project. Jiten states that its downloadable "
                  + "frequency lists are licensed under CC BY-SA 4.0.");
        ImGui.Spacing();

        DrawLinkButton("Jiten", JitenUrl);
        ImGui.SameLine();
        DrawLinkButton("Jiten frequency lists", JitenFrequencyUrl);
        ImGui.SameLine();
        DrawLinkButton("CC BY-SA 4.0", CreativeCommonsBySaUrl);

        ImGui.Spacing();
        ImGui.TextWrapped(
                  "JETHelper does not claim ownership of the bundled "
                  + "dictionary data. Each archive remains subject to its "
                  + "applicable terms and attribution requirements.");
    }

    private static void DrawUpdateSection()
    {
        ImGui.TextUnformatted("Dictionary updates");
        ImGui.TextWrapped(
                  "Bundled JMdict, KANJIDIC, and Jiten Frequency Global "
                  + "snapshots may be refreshed with future JETHelper "
                  + "releases. Users may also select an additional compatible "
                  + "dictionary folder through /jetconfig.");
        ImGui.TextWrapped(
                  "User-supplied dictionaries are not distributed by "
                  + "JETHelper and remain subject to their respective terms.");
    }

    private static void DrawCommunityDictionarySection()
    {
        ImGui.TextUnformatted("Community dictionary resources");
        ImGui.TextWrapped(
                  "Users looking for additional compatible dictionaries may "
                  + "consult MarvNC's community collection. Only JETHelper's "
                  + "explicitly bundled dictionaries have been reviewed for "
                  + "JETHelper distribution and tested as its supported "
                  + "baseline. Other downloads are used at the user's "
                  + "discretion.");
        ImGui.Spacing();

        DrawLinkButton("MarvNC dictionary repository",
                       MarvDictionaryRepositoryUrl);
        ImGui.SameLine();
        DrawLinkButton("MarvNC download folder", MarvDictionaryFolderUrl);
    }

    private static void DrawRelatedProjectsSection()
    {
        ImGui.TextUnformatted("Related projects and resources");

        DrawLinkButton("Yomitan", YomitanUrl);
        ImGui.SameLine();
        DrawLinkButton("Anki", AnkiUrl);
        ImGui.SameLine();
        DrawLinkButton("AnkiConnect", AnkiConnectUrl);

        DrawLinkButton("Dalamud", DalamudUrl);
    }

    private static void DrawContributorsSection()
    {
        ImGui.TextUnformatted("Contributors");
        ImGui.TextWrapped(
                  "JETHelper was created by Ardianell (@J-Lehrack on GitHub). "
                  + "Additional contributors will be acknowledged as the "
                  + "project grows.");
    }

    private static void DrawJetHelperSection()
    {
        ImGui.TextUnformatted("JETHelper source code");
        ImGui.TextWrapped(
                  "JETHelper's source code is licensed under the GNU Affero "
                  + "General Public License version 3.0 or later.");
        ImGui.Spacing();
        DrawLinkButton("JETHelper repository", JetHelperRepositoryUrl);
    }

    private static void DrawSectionSeparator()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void DrawLinkButton(string label, string url)
    {
        if (!ImGui.Button(label))
            return;

        try {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) {
            Plugin.Log.Warning(ex,
                               "Could not open acknowledgement URL: {Url}",
                               url);
        }
    }
}
