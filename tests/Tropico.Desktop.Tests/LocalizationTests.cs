using System.Globalization;
using Avalonia.Media;
using Tropico.Analysis;
using Tropico.Desktop.Localization;
using Tropico.Desktop.Services;
using Tropico.Desktop.ViewModels;
using Tropico.Localization;

namespace Tropico.Desktop.Tests;

public class TranslatorTests
{
    [Fact]
    public void Indexer_TranslatesKeys_AndTheLanguageCanChange()
    {
        var translator = new Translator();
        Assert.Equal("Overview", translator["ui.overview"]);

        translator.SetLanguage(Languages.French);

        Assert.Equal("Vue d'ensemble", translator["ui.overview"]);
        Assert.Equal("Économie", translator["ui.economy"]);
    }

    [Fact]
    public void SetLanguage_RefreshesTheIndexerBindings_AndNotifies()
    {
        var translator = new Translator();
        var changed = new List<string?>();
        var languageChanged = 0;
        translator.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        translator.LanguageChanged += (_, _) => languageChanged++;

        translator.SetLanguage(Languages.Arabic);
        translator.SetLanguage(Languages.Arabic); // same language: nothing happens

        Assert.Contains("Item[]", changed);
        Assert.Contains(nameof(Translator.IsRightToLeft), changed);
        Assert.Equal(1, languageChanged);
        Assert.True(translator.IsRightToLeft);
    }
}

public class LanguageSettingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tropico-i18n-" + Guid.NewGuid())).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class MemoryStore(string? language = null) : ISettingsStore
    {
        public string? Language { get; set; } = language;
    }

    [Fact]
    public void Resolver_PrefersTheEnvironment_ThenTheSavedChoice_ThenTheSystem_ThenEnglish()
    {
        var french = CultureInfo.GetCultureInfo("fr-FR");
        var japanese = CultureInfo.GetCultureInfo("ja-JP");

        Assert.Equal("it", LanguageResolver.Resolve(new MemoryStore("es"), french, "it").Code);   // environment override
        Assert.Equal("es", LanguageResolver.Resolve(new MemoryStore("es"), french).Code);          // saved choice
        Assert.Equal("fr", LanguageResolver.Resolve(new MemoryStore(), french).Code);              // system language
        Assert.Equal("en", LanguageResolver.Resolve(new MemoryStore(), japanese).Code);            // unsupported: English
        Assert.Equal("es", LanguageResolver.Resolve(new MemoryStore("es"), japanese, "klingon").Code); // bad override falls through
        Assert.Equal("fr", LanguageResolver.Resolve(null, french).Code);                            // no store at all
    }

    [Fact]
    public void FileStore_RoundTripsTheLanguage_AndCreatesItsFolder()
    {
        var path = Path.Combine(_directory, "nested", "settings.json");
        var store = new FileSettingsStore(path);

        Assert.Null(store.Language);
        store.Language = "ar";

        Assert.Equal("ar", new FileSettingsStore(path).Language);
    }

    [Fact]
    public void FileStore_IgnoresACorruptFile()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Null(new FileSettingsStore(path).Language);
    }
}

public class MainViewModelLanguageTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        public string? Language { get; set; }
    }

    private sealed class CountingService(IReadOnlyList<SaveFileItem> saves, Func<IslandReport> report) : ISaveAnalysisService
    {
        public int Analyses { get; private set; }

        public IReadOnlyList<SaveFileItem> ListSaves() => saves;

        public Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
        {
            Analyses++;
            return Task.FromResult(report());
        }
    }

    private static IslandReport Report() => new(
        new IslandSnapshot("island", "t6", new BuildingSummary(3, new Dictionary<string, int>(), new Dictionary<string, int>()),
            new PopulationSummary(1000, 200, 800, 0, 0), 12_000, 0, [], [], [], [])
        {
            EconomyData = new EconomySnapshot(12, [], [], 3_000, 1_200, 800, 300, 0, 0, new Dictionary<string, long>()) { Year = 1930, Month = 3 },
        },
        [new Finding("w", Severity.Warning, Confidence.Uncertain, LocalizedText.Of("finding.balance.negativeMonths", 3, 12)) { Category = "Economy" }]);

    private static readonly SaveFileItem Save = new(@"C:\saves\a.t6sav", "a", new DateTime(2026, 1, 1), 1024);

    private static async Task<(MainViewModel Vm, CountingService Service, MemoryStore Settings)> Started()
    {
        var service = new CountingService([Save], Report);
        var settings = new MemoryStore();
        var vm = new MainViewModel(service, new Translator(), settings);
        await vm.RefreshCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && vm.Report is null; i++) await Task.Delay(10);
        return (vm, service, settings);
    }

    [Fact]
    public async Task StartsInEnglish_WithLeftToRightLayout()
    {
        var (vm, _, _) = await Started();

        Assert.Equal("en", vm.SelectedLanguage.Code);
        Assert.Equal(FlowDirection.LeftToRight, vm.LayoutDirection);
        Assert.Equal("3 of the last 12 monthly balances are negative.", vm.Report!.Findings[0].Message);
    }

    [Fact]
    public async Task ChangingTheLanguage_ReRendersTheReportWithoutAnalysingAgain()
    {
        var (vm, service, settings) = await Started();

        vm.SelectedLanguage = Languages.French;

        Assert.Equal(1, service.Analyses); // the same analysis is only rendered again
        Assert.Equal("3 des 12 derniers bilans mensuels sont négatifs.", vm.Report!.Findings[0].Message);
        Assert.Equal("Avertissement · Incertain", vm.Report.Findings[0].Label);
        Assert.Equal("fr", settings.Language); // remembered for the next run
        Assert.Contains("mois de dépenses", vm.Report.Economy.Runway);
    }

    [Fact]
    public async Task Arabic_MirrorsTheLayout_AndSwitchingBackRestoresIt()
    {
        var (vm, _, settings) = await Started();
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.SelectedLanguage = Languages.Arabic;
        Assert.Equal(FlowDirection.RightToLeft, vm.LayoutDirection);
        Assert.Contains(nameof(MainViewModel.LayoutDirection), notified);
        Assert.Equal("ar", settings.Language);
        Assert.Matches(@"[\u0600-\u06FF]", vm.Report!.Findings[0].Message);

        vm.SelectedLanguage = Languages.Italian;
        Assert.Equal(FlowDirection.LeftToRight, vm.LayoutDirection);
    }

    [Fact]
    public async Task StatusAndErrorMessages_FollowTheLanguage()
    {
        var empty = new MainViewModel(new CountingService([], Report), new Translator(), new MemoryStore());
        await empty.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("No Tropico 6 save found.", empty.StatusMessage);

        empty.SelectedLanguage = Languages.Spanish;

        Assert.Equal("No se encontró ninguna partida de Tropico 6.", empty.StatusMessage);
    }

    [Fact]
    public async Task ReadErrors_AreShownInTheCurrentLanguage_AndTranslatedWhenItChanges()
    {
        var service = new FailingService();
        var vm = new MainViewModel(service, new Translator(), new MemoryStore());
        await vm.RefreshCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && vm.ErrorMessage is null; i++) await Task.Delay(10);

        Assert.Equal("Could not read 'a': bad zlib", vm.ErrorMessage);

        vm.SelectedLanguage = Languages.Italian;

        Assert.Equal("Impossibile leggere «a»: bad zlib", vm.ErrorMessage);
    }

    private sealed class FailingService : ISaveAnalysisService
    {
        public IReadOnlyList<SaveFileItem> ListSaves() => [Save];

        public Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromException<IslandReport>(new InvalidDataException("bad zlib"));
    }
}

public class LocalizedViewModelTests
{
    private static IslandSnapshot Snapshot() => new(
        "island", "t6", new BuildingSummary(3, new Dictionary<string, int>(), new Dictionary<string, int>()),
        new PopulationSummary(1000, 200, 800, 0, 0), 1_453_750, 0,
        [new TimePoint(11, 1_000), new TimePoint(12, 1_100)], [], [], [],
        new EconomySnapshot(
            12,
            [new GoodSnapshot("Coal", 1.6, 1.5, 33_409, 27_748, 0, 0, 3, ["Mexico"])],
            [], 3_000, 1_200, 800, 300, 0, 2_000, new Dictionary<string, long>())
        {
            Year = 1934,
            Month = 9,
            RevenueByCategory = new Dictionary<string, long> { ["Exports"] = 2_000, ["Rents"] = 1_000 },
            ExpensesByCategory = new Dictionary<string, long> { ["Wages"] = 800, ["Upkeeps"] = 300 },
            RevenueHistory = [new TimePoint(11, 10), new TimePoint(12, 20)],
            ExpenseHistory = [new TimePoint(11, 5), new TimePoint(12, 6)],
        })
    {
        PoliticsData = PoliticsSnapshot.Empty with
        {
            Era = "WorldWars",
            Factions = [new FactionStanding("Capitalists", -13.5, 10, 20, -10, [new FactionDriver(DriverKind.Edict, "BP_Edict_Food_for_the_People_C", -10)])],
            Demands = [new DemandInfo("BP_T6FactionDemand_REV_8_C", "Rewarded")],
        },
    };

    [Fact]
    public void ResourceNamesAndNumbers_UseTheLanguage()
    {
        var french = Localizer.For(Languages.French);

        var row = new TradeViewModel(Snapshot(), [], french).Goods[0];

        Assert.Equal("Charbon", row.Resource);
        Assert.Equal("33409", new string(row.Stock.Where(char.IsDigit).ToArray()));
        Assert.DoesNotContain(",", row.Stock); // French grouping is not a comma
        Assert.Equal("Coal", new TradeViewModel(Snapshot(), []).Goods[0].Resource);
    }

    [Fact]
    public void ChartDates_UseTheMonthNamesOfTheLanguage()
    {
        var english = new EconomyViewModel(Snapshot(), []).TreasuryRight;
        var french = new EconomyViewModel(Snapshot(), [], Localizer.For(Languages.French)).TreasuryRight;
        var arabic = new EconomyViewModel(Snapshot(), [], Localizer.For(Languages.Arabic)).TreasuryRight;

        Assert.Equal("Sep 1934", english);
        Assert.StartsWith("sept", french);
        Assert.EndsWith("1934", french);
        Assert.Matches(@"[\u0600-\u06FF]+ 1934", arabic!);
    }

    [Fact]
    public void FinanceCategories_FactionsErasAndDemandStatuses_AreTranslated()
    {
        var spanish = Localizer.For(Languages.Spanish);

        var economy = new EconomyViewModel(Snapshot(), [], spanish);
        Assert.Equal(["Exportaciones", "Alquileres"], economy.RevenueBreakdown.Select(r => r.Name));
        Assert.Equal(["Salarios", "Mantenimiento"], economy.ExpenseBreakdown.Select(r => r.Name));

        var politics = new PoliticsViewModel(Snapshot(), [], spanish);
        Assert.Equal("Guerras mundiales", politics.Era);
        Assert.Equal("Capitalistas", politics.Factions[0].Name);
        Assert.Equal("Recompensada", politics.Demands[0].Status);
        Assert.StartsWith("edicto Food for the People -10", politics.Factions[0].Causes.Replace(',', '.')); // game identifiers stay as the game names them
    }

    [Fact]
    public void FindingLabels_AndEvidence_AreRenderedInTheLanguage()
    {
        var finding = new Finding("f", Severity.Info, Confidence.Probable, LocalizedText.Of("finding.wageBurden", 68.0))
        {
            EvidenceTexts = [LocalizedText.Of("evidence.wages", 111_432.0)],
            SuggestionText = LocalizedText.Of("suggest.wageBurden"),
        };

        var italian = new FindingViewModel(finding, Localizer.For(Languages.Italian));

        Assert.Equal("I salari sono il 68 % delle spese.", italian.Message);
        Assert.Equal("Info · Probabile", italian.Label);
        Assert.Equal("Salari: 111.432", italian.Evidence[0]);
        Assert.StartsWith("Controlla il livello salariale", italian.Suggestion);
    }
}
