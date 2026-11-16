using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ShaderCacheCleaner.Helpers;
using ShaderCacheCleaner.Models;
using ShaderCacheCleaner.Services;
using ShaderCacheCleaner.Views;

namespace ShaderCacheCleaner.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxRebootsPerPhase = 2;

    private readonly CleanupLogger _logger;
    private readonly SettingsManager _settings;
    private readonly CacheScanner _scanner;
    private readonly ShaderCacheCleanerService _cleaner;
    private readonly NvidiaManager _nvidia;
    private readonly SystemInfoService _systemInfo;
    private readonly ProcessManager _processes;
    private readonly StartupRecoveryManager _recovery;
    private readonly SystemRebootManager _reboot;
    private readonly IconManager _icons;
    private readonly bool _resumeMode;

    private SessionState _session = new();
    private SystemInfoModel _info = new();
    private CleanupMode _selectedMode = CleanupMode.Cs2Clean;
    private bool _suppressModeScan;
    private bool _isBusy;
    private bool _isCompleted;
    private bool _countdownActive;
    private string _resultModeText = "";
    private int _countdownSeconds;
    private string _statusBadge = "Scan en cours";
    private string _statusBadgeColor = "#F5A623";
    private string _primarySizeText = "—";
    private string _primaryLabel = "CACHE ACTUEL";
    private string _cacheDetailLine = "";
    private double _barFill;
    private Brush _barBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
    private string _stepText = "Initialisation...";
    private string _folderText = "";
    private string _progressText = "";
    private double _progressPercent;
    private string _beforeText = "";
    private string _afterText = "";
    private string _freedText = "";
    private string _reductionText = "";
    private bool _showResultPanel;
    private string _warningText = "Le cache NVIDIA est global. Le nettoyage peut temporairement ralentir d'autres jeux (recompilation des shaders).";
    private DispatcherTimer? _countdownTimer;
    private Action? _countdownAction;
    private CancellationTokenSource? _scanCts;

    public MainViewModel(
        CleanupLogger logger,
        SettingsManager settings,
        CacheScanner scanner,
        ShaderCacheCleanerService cleaner,
        NvidiaManager nvidia,
        SystemInfoService systemInfo,
        ProcessManager processes,
        StartupRecoveryManager recovery,
        SystemRebootManager reboot,
        IconManager icons,
        bool resumeMode)
    {
        _logger = logger;
        _settings = settings;
        _scanner = scanner;
        _cleaner = cleaner;
        _nvidia = nvidia;
        _systemInfo = systemInfo;
        _processes = processes;
        _recovery = recovery;
        _reboot = reboot;
        _icons = icons;
        _resumeMode = resumeMode;

        StartCleanupCommand = new AsyncRelayCommand(async _ => await StartCleanupAsync(), _ => !IsBusy && !IsCompleted);
        SelectCs2Command = new RelayCommand(_ => SelectedMode = CleanupMode.Cs2Clean, _ => !IsBusy);
        SelectFullCommand = new RelayCommand(_ => SelectedMode = CleanupMode.FullClean, _ => !IsBusy);
        RefreshScanCommand = new AsyncRelayCommand(async _ => await ScanAsync(lockAsBefore: false), _ => !IsBusy && !IsCompleted);
        CancelCountdownCommand = new RelayCommand(_ => CancelCountdown(askContinue: true));
        CreateShortcutCommand = new RelayCommand(_ => TryCreateShortcut());
        OpenSteamCommand = new RelayCommand(_ => ExternalLinks.OpenSteam());
        OpenYouTubeCommand = new RelayCommand(_ => ExternalLinks.OpenYouTube());
        OpenDiscordCommand = new RelayCommand(_ => ExternalLinks.OpenDiscord());
    }

    public AsyncRelayCommand StartCleanupCommand { get; }
    public RelayCommand SelectCs2Command { get; }
    public RelayCommand SelectFullCommand { get; }
    public AsyncRelayCommand RefreshScanCommand { get; }
    public RelayCommand CancelCountdownCommand { get; }
    public RelayCommand CreateShortcutCommand { get; }
    public RelayCommand OpenSteamCommand { get; }
    public RelayCommand OpenYouTubeCommand { get; }
    public RelayCommand OpenDiscordCommand { get; }

    public SystemInfoModel Info
    {
        get => _info;
        set => SetProperty(ref _info, value);
    }

    public CleanupMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (_selectedMode == value)
            {
                RaisePropertyChanged(nameof(IsCs2Selected));
                RaisePropertyChanged(nameof(IsFullSelected));
                return;
            }

            SetProperty(ref _selectedMode, value);
            RaisePropertyChanged(nameof(IsCs2Selected));
            RaisePropertyChanged(nameof(IsFullSelected));
            if (!_suppressModeScan && !_session.BeforeStatsLocked)
                _ = ScanAsync(lockAsBefore: false);
        }
    }

    public bool IsCs2Selected => SelectedMode == CleanupMode.Cs2Clean;
    public bool IsFullSelected => SelectedMode == CleanupMode.FullClean;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            SetProperty(ref _isBusy, value);
            StartCleanupCommand.RaiseCanExecuteChanged();
            RefreshScanCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            SetProperty(ref _isCompleted, value);
            StartCleanupCommand.RaiseCanExecuteChanged();
            RefreshScanCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CountdownActive
    {
        get => _countdownActive;
        set => SetProperty(ref _countdownActive, value);
    }

    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set => SetProperty(ref _countdownSeconds, value);
    }

    public string StatusBadge
    {
        get => _statusBadge;
        set => SetProperty(ref _statusBadge, value);
    }

    public string StatusBadgeColor
    {
        get => _statusBadgeColor;
        set => SetProperty(ref _statusBadgeColor, value);
    }

    public string PrimarySizeText
    {
        get => _primarySizeText;
        set => SetProperty(ref _primarySizeText, value);
    }

    public string PrimaryLabel
    {
        get => _primaryLabel;
        set => SetProperty(ref _primaryLabel, value);
    }

    public string CacheDetailLine
    {
        get => _cacheDetailLine;
        set => SetProperty(ref _cacheDetailLine, value);
    }

    public double BarFill
    {
        get => _barFill;
        set => SetProperty(ref _barFill, value);
    }

    public Brush BarBrush
    {
        get => _barBrush;
        set => SetProperty(ref _barBrush, value);
    }

    public string StepText
    {
        get => _stepText;
        set => SetProperty(ref _stepText, value);
    }

    public string FolderText
    {
        get => _folderText;
        set => SetProperty(ref _folderText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string BeforeText
    {
        get => _beforeText;
        set => SetProperty(ref _beforeText, value);
    }

    public string AfterText
    {
        get => _afterText;
        set => SetProperty(ref _afterText, value);
    }

    public string FreedText
    {
        get => _freedText;
        set => SetProperty(ref _freedText, value);
    }

    public string ReductionText
    {
        get => _reductionText;
        set => SetProperty(ref _reductionText, value);
    }

    public bool ShowResultPanel
    {
        get => _showResultPanel;
        set => SetProperty(ref _showResultPanel, value);
    }

    public string ResultModeText
    {
        get => _resultModeText;
        set => SetProperty(ref _resultModeText, value);
    }

    public string WarningText
    {
        get => _warningText;
        set => SetProperty(ref _warningText, value);
    }

    public string NvidiaDetail => $"GPU : {Info.GpuName}  |  Pilote : {Info.NvidiaDriverVersion}";
    public string SteamDetail => Info.SteamDetected
        ? $"Steam : détecté  |  CS2 : {(Info.Cs2Detected ? "détecté" : "non détecté")}"
        : "Steam : non détecté";
    public string WindowsDetail => Info.WindowsVersion;

    public async Task InitializeAsync()
    {
        if (!AdminHelper.IsAdministrator())
        {
            DarkDialog.Show(Application.Current?.MainWindow,
                "Des droits administrateur sont requis pour nettoyer correctement les caches.",
                "Shader Cache Cleaner");
        }

        Info = _systemInfo.Collect();
        RaisePropertyChanged(nameof(NvidiaDetail));
        RaisePropertyChanged(nameof(SteamDetail));
        RaisePropertyChanged(nameof(WindowsDetail));

        var pending = _recovery.TryLoadPendingSession();
        if (pending is not null && pending.Phase is not CleanupPhase.Idle and not CleanupPhase.Completed and not CleanupPhase.Failed)
        {
            _session = pending;
            // Toujours recharger la taille initiale verrouillée depuis le disque
            var lockedBefore = _settings.LoadBeforeStats() ?? pending.BeforeStats;
            if (lockedBefore is not null)
            {
                _session.BeforeStats = lockedBefore;
                _session.BeforeStatsLocked = true;
            }

            _suppressModeScan = true;
            SelectedMode = pending.Mode;
            _suppressModeScan = false;
            await ResumeWorkflowAsync();
            return;
        }

        if (_resumeMode && pending is null)
            _logger.Info("Lancement --resume sans session active : démarrage normal.");

        ApplyLastResultIfAny();
        await ScanAsync(lockAsBefore: false);
    }

    private void ApplyLastResultIfAny(bool preserveStatus = false)
    {
        var last = _settings.LoadLastResult();
        if (last is null)
            return;

        ResultModeText = $"Mode : {(last.Mode == CleanupMode.FullClean ? "FULL CLEAN" : "CS2 CLEAN")}";
        BeforeText = ByteFormatter.FormatFr(last.BeforeBytes);
        AfterText = ByteFormatter.FormatFr(last.AfterBytes);
        FreedText = ByteFormatter.FormatFr(last.FreedBytes);
        ShowResultPanel = true;
        if (!preserveStatus && last.Success)
        {
            StatusBadge = "Dernier résultat";
            StatusBadgeColor = "#76B900";
        }
    }

    private async Task ScanAsync(bool lockAsBefore)
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        IsBusy = true;
        StatusBadge = "Scan en cours";
        StatusBadgeColor = "#888888";
        StepText = "Mesure réelle des caches...";
        try
        {
            var stats = await Task.Run(() => _scanner.Scan(SelectedMode, _scanCts.Token), _scanCts.Token);
            ApplyCurrentSizeUi(stats, isBefore: true);

            if (lockAsBefore || !_session.BeforeStatsLocked)
            {
                LockBeforeStats(stats);
            }

            StatusBadge = "Avant nettoyage";
            StatusBadgeColor = "#F5A623";
            StepText = "Prêt.";

            // Réaffiche le dernier bilan s'il existe (conservé jusqu'au prochain nettoyage)
            if (!IsCompleted)
                ApplyLastResultIfAny(preserveStatus: true);
        }
        catch (OperationCanceledException)
        {
            StepText = "Scan annulé.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Scan : {ex.Message}");
            StatusBadge = "Erreur";
            StatusBadgeColor = "#E74C3C";
            StepText = "Échec du scan.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LockBeforeStats(CacheStatistics stats)
    {
        _session.BeforeStats = stats.Clone();
        _session.BeforeStatsLocked = true;
        _settings.SaveBeforeStats(stats);
        _logger.Info($"Taille initiale verrouillée : {ByteFormatter.FormatFr(stats.TotalBytes)}");
    }

    private void ApplyCurrentSizeUi(CacheStatistics stats, bool isBefore)
    {
        PrimaryLabel = isBefore ? "CACHE ACTUEL" : "CACHE APRÈS";
        PrimarySizeText = ByteFormatter.FormatFr(stats.TotalBytes);
        CacheDetailLine = BuildDetailLine(stats);
        if (isBefore)
        {
            BarFill = 0.92;
            BarBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
            ShowResultPanel = false;
        }
    }

    private static string BuildDetailLine(CacheStatistics stats) =>
        $"NVIDIA {ByteFormatter.FormatFr(stats.NvidiaBytes)}  ·  CS2 {ByteFormatter.FormatFr(stats.SteamCs2Bytes)}  ·  Steam {ByteFormatter.FormatFr(stats.SteamOtherBytes)}  ·  DX {ByteFormatter.FormatFr(stats.DirectXBytes)}";

    private async Task StartCleanupAsync()
    {
        var confirm = DarkDialog.Show(Application.Current?.MainWindow,
            "Le nettoyage va automatiquement :\n" +
            "• mesurer et enregistrer la taille des caches\n" +
            "• désactiver Shader Cache Size (NVAPI)\n" +
            "• redémarrer, nettoyer, restaurer Driver Default\n" +
            "• redémarrer une seconde fois et afficher le bilan\n\n" +
            "Confirmer le démarrage ?",
            "Démarrer le nettoyage",
            MessageBoxButton.YesNo);

        if (confirm != MessageBoxResult.Yes)
            return;

        var running = _processes.GetRunningTargets();
        if (running.Count > 0)
        {
            var close = DarkDialog.Show(Application.Current?.MainWindow,
                $"Processus détectés : {string.Join(", ", running)}\n\nFermer Steam/CS2 maintenant ?",
                "Processus actifs",
                MessageBoxButton.YesNoCancel);

            if (close == MessageBoxResult.Cancel)
                return;
            if (close == MessageBoxResult.Yes)
                await _processes.CloseTargetsAsync();
        }

        IsBusy = true;

        StepText = "Scan initial et enregistrement des tailles...";
        var before = await Task.Run(() => _scanner.Scan(SelectedMode));
        LockBeforeStats(before);
        ApplyCurrentSizeUi(before, isBefore: true);

        _session = new SessionState
        {
            Mode = SelectedMode,
            SimulationMode = false,
            CreateDesktopShortcut = false,
            Phase = CleanupPhase.Preparing,
            BeforeStats = before.Clone(),
            BeforeStatsLocked = true,
            FirstRebootCount = 0,
            SecondRebootCount = 0
        };
        _settings.SaveSession(_session);
        _recovery.MarkPhase(_session, CleanupPhase.Preparing);

        try
        {
            StepText = "Désactivation NVIDIA Shader Cache Size (automatique)...";
            var backup = _nvidia.CaptureAndDisable();
            _session.NvidiaBackup = backup;
            _settings.SaveSession(_session);

            if (!backup.DisabledSuccessfully)
            {
                Fail("Impossible de désactiver automatiquement Shader Cache Size via NVAPI. Opération annulée — aucune affirmation de succès.");
                return;
            }

            await ContinueAfterNvidiaDisabledAsync();
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async Task ContinueAfterNvidiaDisabledAsync()
    {
        _recovery.MarkPhase(_session, CleanupPhase.NvidiaDisabled);
        EnsureBeforeStatsPersisted();

        var exe = GetExePath();
        _reboot.PrepareAutoStart(exe);
        _session.FirstRebootCount++;
        _recovery.MarkPhase(_session, CleanupPhase.WaitingForFirstReboot);

        if (_session.FirstRebootCount > MaxRebootsPerPhase)
        {
            Fail("Boucle de redémarrage détectée (phase 1). Opération annulée.");
            return;
        }

        StepText = "Premier redémarrage : libération des fichiers de cache.";
        StartCountdown(25, () =>
        {
            _reboot.RequestReboot(5, "Shader Cache Cleaner : premier redémarrage.");
        });
        await Task.CompletedTask;
    }

    private async Task ResumeWorkflowAsync()
    {
        IsBusy = true;
        _suppressModeScan = true;
        SelectedMode = _session.Mode;
        _suppressModeScan = false;
        EnsureBeforeStatsPersisted();

        // Afficher immédiatement la taille initiale conservée
        if (_session.BeforeStats is not null)
        {
            BeforeText = ByteFormatter.FormatFr(_session.BeforeStats.TotalBytes);
            ApplyCurrentSizeUi(_session.BeforeStats, isBefore: true);
            StatusBadge = "Reprise";
            StatusBadgeColor = "#F5A623";
        }

        _logger.Info($"Reprise — phase {_session.Phase} | avant={BeforeText}");

        try
        {
            switch (_session.Phase)
            {
                case CleanupPhase.WaitingForFirstReboot:
                case CleanupPhase.NvidiaDisabled:
                case CleanupPhase.Cleaning:
                    await PerformCleaningPhaseAsync();
                    break;

                case CleanupPhase.RestoringNvidia:
                    await RestoreAndSecondRebootAsync();
                    break;

                case CleanupPhase.WaitingForSecondReboot:
                case CleanupPhase.FinalScan:
                    await PerformFinalScanAsync();
                    break;

                default:
                    await ScanAsync(lockAsBefore: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async Task PerformCleaningPhaseAsync()
    {
        StatusBadge = "Nettoyage";
        StatusBadgeColor = "#F5A623";
        _recovery.MarkPhase(_session, CleanupPhase.Cleaning);
        EnsureBeforeStatsPersisted();

        if (_nvidia.IsCacheDisabled(out var label))
            _logger.Info($"Cache NVIDIA toujours désactivé ({label}).");
        else
            _logger.Warn($"Cache NVIDIA non désactivé ({label}). Nettoyage poursuivi.");

        // Fermer les processus susceptibles de verrouiller les caches
        var running = _processes.GetRunningTargets();
        if (running.Count > 0)
        {
            StepText = $"Fermeture de : {string.Join(", ", running)}...";
            await _processes.CloseTargetsAsync();
        }

        var progress = new Progress<CleanupProgress>(p =>
        {
            StepText = p.CurrentStep;
            FolderText = p.CurrentFolder;
            ProgressPercent = p.Percent;
            ProgressText = $"{p.FilesDeleted} fichiers · {ByteFormatter.FormatFr(p.BytesDeleted)} · {p.ErrorCount} erreurs · {p.Percent:0}%";
        });

        var (files, bytes, errors, critical) = await _cleaner.CleanAsync(
            _session.Mode,
            simulation: false,
            progress,
            CancellationToken.None);

        _session.FilesDeleted = files;
        _session.BytesDeleted = bytes;
        _session.ErrorCount = errors;
        _session.CleanupHadCriticalFailures = critical;

        // Rescan réel juste après suppression
        StepText = "Vérification des tailles après suppression...";
        var postClean = await Task.Run(() => _scanner.Scan(_session.Mode));
        _session.PostCleanStats = postClean;
        EnsureBeforeStatsPersisted();
        _settings.SaveSession(_session);

        var before = _session.BeforeStats ?? new CacheStatistics();
        var freedNow = Math.Max(0, before.TotalBytes - postClean.TotalBytes);
        ShowComparison(before, postClean, provisional: true);

        if (files == 0 && before.TotalBytes > 0 && freedNow == 0)
        {
            _session.CleanupHadCriticalFailures = true;
            _logger.Warn("Aucune réduction mesurée après nettoyage.");
        }

        await RestoreAndSecondRebootAsync();
    }

    private async Task RestoreAndSecondRebootAsync()
    {
        _recovery.MarkPhase(_session, CleanupPhase.RestoringNvidia);
        EnsureBeforeStatsPersisted();
        StepText = "Restauration NVIDIA → Par défaut du pilote...";

        if (_session.NvidiaBackup is not null)
        {
            var restored = _nvidia.RestoreToDriverDefault(_session.NvidiaBackup);
            _settings.SaveSession(_session);
            if (!restored)
            {
                Fail("Échec de la restauration automatique NVIDIA (Driver Default). Le second redémarrage est annulé pour éviter un état incorrect.");
                return;
            }
        }

        var exe = GetExePath();
        _reboot.PrepareAutoStart(exe);
        _session.SecondRebootCount++;
        _recovery.MarkPhase(_session, CleanupPhase.WaitingForSecondReboot);

        if (_session.SecondRebootCount > MaxRebootsPerPhase)
        {
            Fail("Boucle de redémarrage détectée (phase 2). Opération annulée.");
            return;
        }

        StepText = "Second redémarrage : finalisation et mesure finale.";
        StartCountdown(25, () =>
        {
            _reboot.RequestReboot(5, "Shader Cache Cleaner : second redémarrage.");
        });
        await Task.CompletedTask;
    }

    private async Task PerformFinalScanAsync()
    {
        _recovery.MarkPhase(_session, CleanupPhase.FinalScan);
        StatusBadge = "Scan final";
        StatusBadgeColor = "#888888";
        StepText = "Rescan réel après redémarrage...";

        // Ne jamais rescanner pour écraser BeforeStats
        var before = _settings.LoadBeforeStats() ?? _session.BeforeStats;
        if (before is null)
        {
            before = new CacheStatistics();
            _logger.Warn("Mesure initiale introuvable.");
        }
        else
        {
            _session.BeforeStats = before;
            _session.BeforeStatsLocked = true;
        }

        var after = await Task.Run(() => _scanner.Scan(_session.Mode));
        _session.AfterStats = after;
        _settings.SaveSession(_session);

        var freed = Math.Max(0, before.TotalBytes - after.TotalBytes);
        ShowComparison(before, after, provisional: false);

        bool success = !_session.CleanupHadCriticalFailures
                       && (_session.FilesDeleted > 0 || freed > 0 || before.TotalBytes == 0);

        if (success)
        {
            StatusBadge = "Terminé";
            StatusBadgeColor = "#76B900";
            BarBrush = new SolidColorBrush(Color.FromRgb(0x76, 0xB9, 0x00));
            AnimateBar(before.TotalBytes <= 0 ? 0.05 : Math.Clamp(after.TotalBytes / (double)Math.Max(1, before.TotalBytes), 0.04, 1.0));
            StepText = after.TotalBytes > 0 && freed > 0
                ? "Nettoyage terminé. Une partie du cache a pu être recréée (taille réelle affichée)."
                : "Nettoyage terminé.";
        }
        else
        {
            StatusBadge = "Échec partiel";
            StatusBadgeColor = "#E74C3C";
            BarBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            BarFill = 0.5;
            StepText = "Le nettoyage n'a pas pu être confirmé (fichiers encore présents ou erreurs).";
        }

        _session.PartialRecreationDetected = after.TotalBytes > 0 && freed > 0;
        _settings.SaveLastResult(new LastCleanupResult
        {
            Mode = _session.Mode,
            BeforeBytes = before.TotalBytes,
            AfterBytes = after.TotalBytes,
            FreedBytes = freed,
            ReductionPercent = before.TotalBytes > 0 ? freed * 100.0 / before.TotalBytes : 0,
            CompletedAt = DateTime.UtcNow,
            Success = success
        });

        _recovery.UnregisterResumeTask();
        _recovery.MarkPhase(_session, CleanupPhase.Completed);
        IsCompleted = true;
        IsBusy = false;

        // Efface la session temporaire, conserve le bilan
        _settings.ClearTemporaryData();
    }

    private void ShowComparison(CacheStatistics before, CacheStatistics after, bool provisional)
    {
        var freed = Math.Max(0, before.TotalBytes - after.TotalBytes);
        var reduction = before.TotalBytes > 0 ? freed * 100.0 / before.TotalBytes : 0;

        ResultModeText = $"Mode : {(_session.Mode == CleanupMode.FullClean ? "FULL CLEAN" : "CS2 CLEAN")}";
        BeforeText = ByteFormatter.FormatFr(before.TotalBytes);
        AfterText = ByteFormatter.FormatFr(after.TotalBytes);
        FreedText = ByteFormatter.FormatFr(freed);
        ReductionText = $"{reduction:0.#} %".Replace('.', ',');
        ShowResultPanel = true;

        PrimaryLabel = provisional ? "CACHE APRÈS SUPPRESSION" : "CACHE APRÈS";
        PrimarySizeText = AfterText;
        CacheDetailLine = BuildDetailLine(after);

        _logger.Info($"Comparatif{(provisional ? " provisoire" : "")} : mode={ResultModeText}, avant={BeforeText}, après={AfterText}, libéré={FreedText}");
    }

    private void EnsureBeforeStatsPersisted()
    {
        if (_session.BeforeStats is null)
        {
            _session.BeforeStats = _settings.LoadBeforeStats();
        }

        if (_session.BeforeStats is not null)
        {
            _session.BeforeStatsLocked = true;
            _settings.SaveBeforeStats(_session.BeforeStats);
            _settings.SaveSession(_session);
        }
    }

    private void AnimateBar(double target)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double current = Math.Max(BarFill, 0.2);
        timer.Tick += (_, _) =>
        {
            current += (target - current) * 0.08;
            if (Math.Abs(current - target) < 0.005)
            {
                BarFill = target;
                timer.Stop();
            }
            else
            {
                BarFill = current;
            }
        };
        timer.Start();
    }

    private void StartCountdown(int seconds, Action onComplete)
    {
        CancelCountdown(askContinue: false);
        CountdownSeconds = seconds;
        CountdownActive = true;
        IsBusy = false;
        _countdownAction = onComplete;
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            CountdownSeconds--;
            if (CountdownSeconds > 0)
                return;

            _countdownTimer.Stop();
            CountdownActive = false;
            var action = _countdownAction;
            _countdownAction = null;
            action?.Invoke();
        };
        _countdownTimer.Start();
    }

    private void CancelCountdown(bool askContinue)
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
        CountdownActive = false;
        var pendingAction = _countdownAction;
        _countdownAction = null;
        _reboot.CancelPendingReboot();

        if (!askContinue || pendingAction is null)
            return;

        StepText = "Redémarrage annulé.";
        var result = DarkDialog.Show(Application.Current?.MainWindow,
            "Redémarrage annulé.\n\nRedémarrer Windows maintenant pour continuer l'opération ?",
            "Redémarrage",
            MessageBoxButton.YesNo);

        if (result == MessageBoxResult.Yes)
            pendingAction.Invoke();
        else
            StepText = "État sauvegardé. Relancez l'application après un redémarrage pour reprendre.";
    }

    private void Fail(string message)
    {
        _session.ErrorMessage = message;
        _session.Phase = CleanupPhase.Failed;
        EnsureBeforeStatsPersisted();
        _settings.SaveSession(_session);
        StatusBadge = "Échec";
        StatusBadgeColor = "#E74C3C";
        BarBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
        StepText = message;
        _logger.Error(message);
        IsBusy = false;
        _recovery.UnregisterResumeTask();

        try
        {
            if (_session.NvidiaBackup is not null)
                _nvidia.RestoreToDriverDefault(_session.NvidiaBackup);
        }
        catch
        {
            // ignore
        }

        DarkDialog.Show(Application.Current?.MainWindow, message, "Échec");
    }

    private static string GetExePath() =>
        Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Chemin EXE introuvable.");

    private void TryCreateShortcut()
    {
        try
        {
            var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is null) return;
            _icons.CreateDesktopShortcut(exe);
            _logger.Info("Raccourci bureau créé.");
            DarkDialog.Show(Application.Current?.MainWindow, "Raccourci créé sur le bureau.", "Raccourci");
        }
        catch (Exception ex)
        {
            DarkDialog.Show(Application.Current?.MainWindow, ex.Message, "Raccourci");
        }
    }
}
