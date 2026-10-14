using VolumeKeeper;
using System.Reflection;

var speaker = new DeviceInfo { EndpointId = "endpoint-speaker", StableId = "stable-speaker", Name = "同じ名前" };
var headphones = new DeviceInfo { EndpointId = "endpoint-headphones", StableId = "stable-headphones", Name = "同じ名前" };
var settings = new AppSettings();
settings.Save(speaker, 0.18f);
settings.Save(headphones, 0.42f);
Assert(settings.Profiles.Count == 2, "同名の機器は別々に保存する");
Assert(settings.Find(speaker)?.Volume == 0.18f, "スピーカーの音量を取得する");
Assert(settings.Find(headphones)?.Volume == 0.42f, "イヤホンの音量を取得する");

var updatedSpeaker = new DeviceInfo
{
    EndpointId = "endpoint-speaker-after-update", StableId = "stable-speaker", Name = "新しい表示名"
};
settings.Save(updatedSpeaker, 0.25f);
Assert(settings.Profiles.Count == 2, "安定IDでドライバー更新後の機器を対応付ける");
Assert(settings.Find(updatedSpeaker)?.EndpointId == updatedSpeaker.EndpointId, "新しい出力先IDを保存する");
Assert(settings.Find(updatedSpeaker)?.Name == "新しい表示名", "表示名を更新する");

var sameNameOnly = new DeviceInfo { EndpointId = "unseen", Name = "同じ名前" };
Assert(settings.Find(sameNameOnly) is null, "機器名だけでは対応付けない");
var reusedEndpoint = new DeviceInfo
{
    EndpointId = updatedSpeaker.EndpointId, StableId = "different-stable-id", Name = "別の機器"
};
Assert(settings.Find(reusedEndpoint) is null, "安定IDが食い違う場合は音量を適用しない");

Assert(!VolumePolicy.ShouldApplyOnSwitch(true, false, null, "speaker", false),
    "アプリ起動時は保存音量を適用しない");
Assert(VolumePolicy.ShouldApplyOnSwitch(true, true, "speaker", "headphones", false),
    "機器の切り替え時に保存音量を適用する");
Assert(!VolumePolicy.ShouldApplyOnSwitch(true, true, "speaker", "speaker", false),
    "音声サービスの再接続では保存音量を再適用しない");
Assert(!VolumePolicy.ShouldApplyOnSwitch(true, true, "speaker", "headphones", true),
    "一時停止中は保存音量を適用しない");
Assert(VolumePercent.ToDecimal(0.0001f) == 0.01m, "1%未満を正確に表示する");
Assert(VolumePercent.ToDecimal(0.001f) == 0.1m, "小数の音量を表示する");

var loudPeak = LoudnessPolicy.NextPeak(0, 0.8f);
Assert(LoudnessPolicy.InitialVolume(1f) == LoudnessPolicy.TargetPeak,
    "新しいセッションは最初のピーク測定前から音量を抑える");
Assert(LoudnessPolicy.InitialVolume(0.08f) == 0.08f,
    "元から小さいアプリ音量は上げない");
Assert(LoudnessPolicy.NextVolume(1f, LoudnessPolicy.InitialVolume(1f), 0f, 0f) ==
       LoudnessPolicy.TargetPeak, "再生前の無音中は安全な音量を維持する");
var quieter = LoudnessPolicy.NextVolume(1f, 1f, loudPeak, 0.8f);
Assert(Math.Abs(quieter - 0.15f) < 0.0001f, "大きな音は目標ピークに合わせて下げる");
Assert(LoudnessPolicy.NextVolume(1f, quieter, loudPeak, 0f) == quieter,
    "無音中に音量を上げない");
Assert(LoudnessPolicy.NextVolume(0.1f, 0.1f, 0.8f, 0.8f) == 0.1f,
    "開始時のアプリ音量を上限として維持する");
var recovered = LoudnessPolicy.NextVolume(1f, quieter, 0.2f, 0.2f);
Assert(recovered > quieter && recovered <= quieter + LoudnessPolicy.RecoveryStep + 0.0001f,
    "音量を上げるときは段階的に戻す");

var baseMethods = typeof(IAudioSessionControl).GetMethods(BindingFlags.Public |
    BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(method => method.MetadataToken).ToArray();
var derivedMethods = typeof(IAudioSessionControl2).GetMethods(BindingFlags.Public |
    BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(method => method.MetadataToken).ToArray();
Assert(baseMethods.Length == 9 && derivedMethods.Length == 14,
    "COM 音声セッションの継承メソッドを vtable に再宣言する");
for (var index = 0; index < baseMethods.Length; index++)
{
    var original = baseMethods[index];
    var redeclared = derivedMethods[index];
    Assert(original.Name == redeclared.Name &&
        original.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
            redeclared.GetParameters().Select(parameter => parameter.ParameterType)),
        $"COM vtable のスロット {index + 3} が一致する");
}
Assert(derivedMethods[10].Name == "GetSessionInstanceIdentifier" &&
       derivedMethods[12].Name == "IsSystemSoundsSession",
    "拡張メソッドは継承メソッドの後に並ぶ");

settings.Save(headphones, 0.0001f);
Assert(Math.Abs(settings.Find(headphones)!.Volume - 0.0001f) < VolumePolicy.ChangeTolerance,
    "1%未満の音量を保存する");

var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VolumeKeeperTests", Guid.NewGuid().ToString("N"));
var store = new SettingsStore(System.IO.Path.Combine(directory, "settings.json"));
try
{
    settings.AutoLevelEnabled = true;
    store.Save(settings);
    var loaded = store.Load(out var warning);
    Assert(warning is null && loaded.Profiles.Count == 2, "設定を再起動後に読み戻せる");
    Assert(loaded.AutoLevelEnabled, "再生音の自動調整設定を読み戻せる");
    Assert(loaded.Find(updatedSpeaker)?.Volume == 0.25f, "保存音量を維持する");
    Assert(Math.Abs(loaded.Find(headphones)!.Volume - 0.0001f) < VolumePolicy.ChangeTolerance,
        "1%未満の音量を再起動後に読み戻せる");
    File.WriteAllText(store.Path, "{broken");
    var fallback = store.Load(out warning);
    Assert(fallback.Profiles.Count == 0 && warning is not null, "破損した設定で起動できる");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

Console.WriteLine("VolumeKeeper logic tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
