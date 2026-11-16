using System.Runtime.InteropServices;
using System.Text;
using ShaderCacheCleaner.Models;

namespace ShaderCacheCleaner.Services;

/// <summary>
/// Automatisation exclusive de « Shader Cache - Cache Size » (0x00AC8497 / 11306135).
/// Off = 0 pendant le nettoyage. Restauration via NvAPI_DRS_RestoreProfileDefaultSetting uniquement.
/// Jamais Unlimited (0xFFFFFFFF). Jamais de mode manuel.
/// </summary>
public sealed class NvidiaManager
{
    private const uint IdInitialize = 0x0150E828;
    private const uint IdDrsCreateSession = 0x0694D52E;
    private const uint IdDrsDestroySession = 0xDAD9CFF8;
    private const uint IdDrsLoadSettings = 0x375DBD6B;
    private const uint IdDrsSaveSettings = 0xFCBC7E14;
    private const uint IdDrsGetBaseProfile = 0xDA8466A0;
    private const uint IdDrsGetCurrentGlobalProfile = 0x617BFF9F;
    private const uint IdDrsGetSettingNew = 0xEA99498D;
    private const uint IdDrsGetSettingOld = 0x73BF8338;
    private const uint IdDrsSetSettingNew = 0x8A2CF5F5;
    private const uint IdDrsSetSettingOld = 0x577DD202;
    private const uint IdDrsRestoreProfileDefaultSetting = 0x53F0381E;
    private const uint IdDrsRestoreProfileDefaultSettingAlt = 0x7DD5B261;
    private const uint IdDrsDeleteProfileSetting = 0xE4A26362;
    private const uint IdSysGetDriverAndBranchVersion = 0x2926AAAD;

    /// <summary>Shader Cache - Cache Size (décimal 11306135).</summary>
    public const uint SettingShaderCacheMaxSize = 0x00AC8497;
    private const uint ValueOff = 0x00000000;
    private const uint ValueUnlimited = 0xFFFFFFFF;

    private const int NvapiStatusOk = 0;
    private const int NvapiStatusSettingNotFound = -160;
    private const int UnicodeMax = 2048;

    private readonly CleanupLogger _logger;
    private bool _nvapiReady;
    private bool _nvapiAttempted;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsCreateSessionDelegate(out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsDestroySessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsLoadSettingsDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsSaveSettingsDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsGetProfileDelegate(IntPtr session, out IntPtr profile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsGetSettingDelegate(IntPtr session, IntPtr profile, uint settingId, ref NvDrsSetting setting, ref uint reserved);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsSetSettingDelegate(IntPtr session, IntPtr profile, ref NvDrsSetting setting, uint reserved1, uint reserved2);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsRestoreDefaultSettingDelegate(IntPtr session, IntPtr profile, uint settingId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrsDeleteProfileSettingDelegate(IntPtr session, IntPtr profile, uint settingId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SysGetDriverVersionDelegate(out uint driverVersion, StringBuilder buildBranch);

    private NvAPI_InitializeDelegate? _initialize;
    private DrsCreateSessionDelegate? _createSession;
    private DrsDestroySessionDelegate? _destroySession;
    private DrsLoadSettingsDelegate? _loadSettings;
    private DrsSaveSettingsDelegate? _saveSettings;
    private DrsGetProfileDelegate? _getBaseProfile;
    private DrsGetProfileDelegate? _getCurrentGlobalProfile;
    private DrsGetSettingDelegate? _getSetting;
    private DrsSetSettingDelegate? _setSetting;
    private DrsRestoreDefaultSettingDelegate? _restoreDefaultSetting;
    private DrsDeleteProfileSettingDelegate? _deleteProfileSetting;
    private SysGetDriverVersionDelegate? _getDriverVersion;

    public NvidiaManager(CleanupLogger logger) => _logger = logger;

    public string? GetDriverVersion()
    {
        if (!EnsureNvapi() || _getDriverVersion is null)
            return null;

        try
        {
            var branch = new StringBuilder(64);
            if (_getDriverVersion(out uint version, branch) != NvapiStatusOk)
                return null;

            return $"{version / 100}.{version % 100:00}";
        }
        catch (Exception ex)
        {
            _logger.Warn($"Version pilote : {ex.Message}");
            return null;
        }
    }

    public string GetShaderCacheStateLabel()
    {
        if (!TryReadCacheSize(out var value, out var found, out _))
            return "Non vérifiable";
        return !found ? "Par défaut du pilote" : FormatCacheSizeLabel(value);
    }

    public bool IsCacheDisabled(out string label)
    {
        if (!TryReadCacheSize(out var value, out var found, out _))
        {
            label = "Non vérifiable";
            return false;
        }

        if (!found)
        {
            label = "Par défaut du pilote";
            return false;
        }

        label = FormatCacheSizeLabel(value);
        return value == ValueOff;
    }

    /// <summary>Lit, enregistre, met Off (0), sauvegarde, vérifie. 100 % automatique.</summary>
    public NvidiaBackup CaptureAndDisable(bool allowManualFallback = false)
    {
        _ = allowManualFallback; // ignoré — jamais de flux manuel
        var backup = new NvidiaBackup { WasAvailable = EnsureNvapi(), UsedManualAssistedFlow = false };

        if (!EnsureNvapi())
        {
            backup.DisabledSuccessfully = false;
            backup.Notes = "NVAPI indisponible. Impossible de modifier Shader Cache Size automatiquement.";
            _logger.Error(backup.Notes);
            return backup;
        }

        if (!TryReadCacheSize(out var current, out var found, out var readStatus))
        {
            backup.DisabledSuccessfully = false;
            backup.Notes = $"Lecture SettingID 0x00AC8497 échouée (status={readStatus}).";
            _logger.Error(backup.Notes);
            return backup;
        }

        backup.OriginalSettingValue = found ? current : null;
        backup.OriginalSettingLabel = found ? FormatCacheSizeLabel(current) : "Par défaut du pilote";
        _logger.Info($"Shader Cache Size lu : {backup.OriginalSettingLabel} (brut={(found ? current.ToString() : "absent")})");

        if (found && current == ValueOff)
        {
            backup.DisabledSuccessfully = true;
            backup.Notes = "Déjà Off (vérifié).";
            return backup;
        }

        if (!TryWriteCacheSize(ValueOff, out var writeStatus))
        {
            backup.DisabledSuccessfully = false;
            backup.Notes = $"Écriture Off échouée (status={writeStatus}).";
            _logger.Error(backup.Notes);
            return backup;
        }

        if (!TryReadCacheSize(out var verify, out var verifyFound, out var verifyStatus) || !verifyFound || verify != ValueOff)
        {
            backup.DisabledSuccessfully = false;
            backup.Notes = $"Vérification Off échouée (status={verifyStatus}, found={verifyFound}, value={(verifyFound ? verify.ToString() : "n/a")}).";
            _logger.Error(backup.Notes);
            return backup;
        }

        backup.DisabledSuccessfully = true;
        backup.Notes = "Shader Cache Size = Off (0) vérifié.";
        _logger.Info(backup.Notes);
        return backup;
    }

    /// <summary>
    /// Restaure uniquement via NvAPI_DRS_RestoreProfileDefaultSetting + SaveSettings.
    /// Ne jamais écrire Unlimited. Ne restaure pas tous les profils.
    /// </summary>
    public bool RestoreToDriverDefault(NvidiaBackup backup)
    {
        _logger.Info($"Restauration Driver Default (précédent : {backup.OriginalSettingLabel}).");
        backup.UsedManualAssistedFlow = false;

        if (!TryRestoreDefaultSetting(out var restoreStatus))
        {
            backup.RestoredSuccessfully = false;
            backup.Notes += $" RestoreProfileDefaultSetting échoué (status={restoreStatus}).";
            _logger.Error(backup.Notes);
            return false;
        }

        if (!TryReadCacheSize(out var verify, out var found, out var verifyStatus))
        {
            backup.RestoredSuccessfully = false;
            backup.Notes += $" Vérification restauration impossible (status={verifyStatus}).";
            _logger.Error(backup.Notes);
            return false;
        }

        if (found && verify == ValueUnlimited)
        {
            backup.RestoredSuccessfully = false;
            backup.Notes += " Illimité détecté après restauration — interdit.";
            _logger.Error(backup.Notes);
            // Tentative de correction : restore à nouveau, jamais écrire Unlimited
            TryRestoreDefaultSetting(out _);
            return false;
        }

        if (found && verify == ValueOff)
        {
            backup.RestoredSuccessfully = false;
            backup.Notes += " Toujours Off après restauration.";
            _logger.Error(backup.Notes);
            return false;
        }

        // Succès : setting absent (= défaut pilote) OU valeur non-Off / non-Unlimited
        backup.RestoredSuccessfully = true;
        backup.Notes += found
            ? $" Driver Default restauré (vérifié : {FormatCacheSizeLabel(verify)})."
            : " Driver Default restauré (paramètre retiré du profil).";
        _logger.Info(backup.Notes);
        return true;
    }

    public bool Restore(NvidiaBackup backup) => RestoreToDriverDefault(backup);

    public static string FormatCacheSizeLabel(uint value) => value switch
    {
        ValueOff => "Désactivé",
        ValueUnlimited => "Illimité",
        0x00004000 => "Par défaut du pilote (16 Go)",
        0x00000080 => "128 Mo",
        0x00000100 => "256 Mo",
        0x00000200 => "512 Mo",
        0x00000400 => "1 Go",
        0x00001000 => "4 Go",
        0x00001400 => "5 Go",
        0x00002000 => "8 Go",
        0x00002800 => "10 Go",
        0x00019000 => "100 Go",
        _ => $"{value} Mo"
    };

    private bool TryRestoreDefaultSetting(out int status)
    {
        status = -1;
        if (!EnsureNvapi() || _createSession is null || _loadSettings is null || _saveSettings is null)
            return false;

        if (_restoreDefaultSetting is null && _deleteProfileSetting is null)
            return false;

        IntPtr session = IntPtr.Zero;
        try
        {
            status = _createSession(out session);
            if (status != NvapiStatusOk) return false;

            status = _loadSettings(session);
            if (status != NvapiStatusOk) return false;

            if (!TryGetProfile(session, out var profile))
            {
                status = -1;
                return false;
            }

            // Priorité : RestoreProfileDefaultSetting (comme demandé)
            if (_restoreDefaultSetting is not null)
            {
                status = _restoreDefaultSetting(session, profile, SettingShaderCacheMaxSize);
                _logger.Info($"RestoreProfileDefaultSetting status={status}");
            }

            // Repli strictement limité à DeleteProfileSetting (remet le défaut), jamais Set Unlimited
            if (status != NvapiStatusOk && _deleteProfileSetting is not null)
            {
                status = _deleteProfileSetting(session, profile, SettingShaderCacheMaxSize);
                _logger.Info($"DeleteProfileSetting status={status}");
            }

            if (status != NvapiStatusOk)
                return false;

            status = _saveSettings(session);
            return status == NvapiStatusOk;
        }
        catch (Exception ex)
        {
            _logger.Error($"RestoreDefault : {ex.Message}");
            return false;
        }
        finally
        {
            if (session != IntPtr.Zero)
                _destroySession?.Invoke(session);
        }
    }

    private bool TryReadCacheSize(out uint value, out bool found, out int status)
    {
        value = 0;
        found = false;
        status = -1;

        if (!EnsureNvapi() || _createSession is null || _loadSettings is null || _getSetting is null)
            return false;

        IntPtr session = IntPtr.Zero;
        try
        {
            status = _createSession(out session);
            if (status != NvapiStatusOk) return false;

            status = _loadSettings(session);
            if (status != NvapiStatusOk) return false;

            if (!TryGetProfile(session, out var profile))
            {
                status = -1;
                return false;
            }

            var setting = CreateEmptySetting();
            uint reserved = 0;
            status = _getSetting(session, profile, SettingShaderCacheMaxSize, ref setting, ref reserved);

            if (status == NvapiStatusSettingNotFound)
            {
                found = false;
                return true;
            }

            if (status != NvapiStatusOk)
            {
                _logger.Warn($"GetSetting 0x00AC8497 status={status}");
                return false;
            }

            found = true;
            value = setting.currentValue.DwordValue;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Lecture : {ex.Message}");
            return false;
        }
        finally
        {
            if (session != IntPtr.Zero)
                _destroySession?.Invoke(session);
        }
    }

    private bool TryWriteCacheSize(uint value, out int status)
    {
        status = -1;
        if (value == ValueUnlimited)
        {
            _logger.Error("Refus d'écrire Unlimited.");
            return false;
        }

        if (!EnsureNvapi() || _createSession is null || _loadSettings is null || _setSetting is null || _saveSettings is null)
            return false;

        IntPtr session = IntPtr.Zero;
        try
        {
            status = _createSession(out session);
            if (status != NvapiStatusOk) return false;

            status = _loadSettings(session);
            if (status != NvapiStatusOk) return false;

            if (!TryGetProfile(session, out var profile))
            {
                status = -1;
                return false;
            }

            var setting = CreateEmptySetting();
            setting.settingId = SettingShaderCacheMaxSize;
            setting.settingType = 0; // DWORD
            setting.settingLocation = 0;
            setting.currentValue = NvDrsSettingUnion.FromDword(value);

            status = _setSetting(session, profile, ref setting, 0, 0);
            if (status != NvapiStatusOk)
            {
                _logger.Warn($"SetSetting status={status}");
                return false;
            }

            status = _saveSettings(session);
            return status == NvapiStatusOk;
        }
        catch (Exception ex)
        {
            _logger.Error($"Écriture : {ex.Message}");
            return false;
        }
        finally
        {
            if (session != IntPtr.Zero)
                _destroySession?.Invoke(session);
        }
    }

    private bool TryGetProfile(IntPtr session, out IntPtr profile)
    {
        profile = IntPtr.Zero;
        if (_getCurrentGlobalProfile is not null &&
            _getCurrentGlobalProfile(session, out profile) == NvapiStatusOk &&
            profile != IntPtr.Zero)
            return true;

        if (_getBaseProfile is not null &&
            _getBaseProfile(session, out profile) == NvapiStatusOk &&
            profile != IntPtr.Zero)
            return true;

        return false;
    }

    private bool EnsureNvapi()
    {
        if (_nvapiAttempted)
            return _nvapiReady;

        _nvapiAttempted = true;
        try
        {
            _initialize = Resolve<NvAPI_InitializeDelegate>(IdInitialize);
            _createSession = Resolve<DrsCreateSessionDelegate>(IdDrsCreateSession);
            _destroySession = Resolve<DrsDestroySessionDelegate>(IdDrsDestroySession);
            _loadSettings = Resolve<DrsLoadSettingsDelegate>(IdDrsLoadSettings);
            _saveSettings = Resolve<DrsSaveSettingsDelegate>(IdDrsSaveSettings);
            _getBaseProfile = Resolve<DrsGetProfileDelegate>(IdDrsGetBaseProfile);
            _getCurrentGlobalProfile = Resolve<DrsGetProfileDelegate>(IdDrsGetCurrentGlobalProfile);
            _getSetting = Resolve<DrsGetSettingDelegate>(IdDrsGetSettingNew, IdDrsGetSettingOld);
            _setSetting = Resolve<DrsSetSettingDelegate>(IdDrsSetSettingNew, IdDrsSetSettingOld);
            _restoreDefaultSetting = Resolve<DrsRestoreDefaultSettingDelegate>(IdDrsRestoreProfileDefaultSettingAlt, IdDrsRestoreProfileDefaultSetting);
            _deleteProfileSetting = Resolve<DrsDeleteProfileSettingDelegate>(IdDrsDeleteProfileSetting);
            _getDriverVersion = Resolve<SysGetDriverVersionDelegate>(IdSysGetDriverAndBranchVersion);

            if (_initialize is null)
            {
                _logger.Error("nvapi_QueryInterface(Initialize) introuvable.");
                return false;
            }

            var init = _initialize();
            if (init != NvapiStatusOk)
            {
                _logger.Error($"NvAPI_Initialize status={init}");
                return false;
            }

            if (_createSession is null || _getSetting is null || _setSetting is null || _saveSettings is null)
            {
                _logger.Error("Fonctions DRS essentielles manquantes.");
                return false;
            }

            _nvapiReady = true;
            _logger.Info($"NVAPI prêt. SettingVER=0x{NvDrsSettingVer:X8} SizeOf={Marshal.SizeOf<NvDrsSetting>()}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"NVAPI : {ex.Message}");
            _nvapiReady = false;
            return false;
        }
    }

    private static T? Resolve<T>(uint id, uint? fallback = null) where T : Delegate
    {
        var ptr = nvapi_QueryInterface(id);
        if (ptr == IntPtr.Zero && fallback.HasValue)
            ptr = nvapi_QueryInterface(fallback.Value);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr nvapi_QueryInterface(uint interfaceId);

    // Convention NVIDIA / Profile Inspector : sizeof | (ver << 16)
    private static uint MakeNvapiVersion<T>(uint ver) =>
        (uint)Marshal.SizeOf<T>() | (ver << 16);

    private static uint NvDrsSettingVer => MakeNvapiVersion<NvDrsSetting>(1);

    private static NvDrsSetting CreateEmptySetting() => new()
    {
        version = NvDrsSettingVer,
        settingName = string.Empty,
        predefinedValue = NvDrsSettingUnion.Empty(),
        currentValue = NvDrsSettingUnion.Empty()
    };

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode, Size = 4100)]
    private struct NvDrsSettingUnion
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4100)]
        public byte[] RawData;

        public uint DwordValue => RawData is { Length: >= 4 } ? BitConverter.ToUInt32(RawData, 0) : 0;

        public static NvDrsSettingUnion FromDword(uint value)
        {
            var u = Empty();
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, u.RawData!, 0, 4);
            return u;
        }

        public static NvDrsSettingUnion Empty() => new() { RawData = new byte[4100] };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    private struct NvDrsSetting
    {
        public uint version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = UnicodeMax)]
        public string settingName;
        public uint settingId;
        public uint settingType;
        public uint settingLocation;
        public uint isCurrentPredefined;
        public uint isPredefinedValid;
        public NvDrsSettingUnion predefinedValue;
        public NvDrsSettingUnion currentValue;
    }
}
