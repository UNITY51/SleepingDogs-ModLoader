using System.IO.Compression;
using SDModLoader.Core;

// Лёгкий самотест без внешних пакетов: dotnet run --project tests/SDModLoader.SelfTest
int failed = 0, passed = 0;
void Check(bool ok, string name) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + name); } }

var tmp = Path.Combine(Path.GetTempPath(), "sdml-test-" + Guid.NewGuid().ToString("N"));
var game = Path.Combine(tmp, "game"); Directory.CreateDirectory(game);
var paths = new AppPaths(Path.Combine(tmp, "Data"));
var lib = new ModLibrary(paths);
var state = new AppState();
var inst = new ModInstaller(paths, state, () => game);

string MakeZip(string name, Dictionary<string, string> files)
{
    var p = Path.Combine(tmp, name);
    using var z = ZipFile.Open(p, ZipArchiveMode.Create);
    foreach (var (k, v) in files) { var e = z.CreateEntry(k); using var w = new StreamWriter(e.Open()); w.Write(v); }
    return p;
}
string Read(string rel) => File.ReadAllText(Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar)));
bool Exists(string rel) => File.Exists(Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar)));
const string F = "RedirectorData/Characters_New/shirt.perm.bin";

// оригинальный файл игры
Directory.CreateDirectory(Path.Combine(game, "RedirectorData", "Characters_New"));
File.WriteAllText(Path.Combine(game, F.Replace('/', Path.DirectorySeparatorChar)), "ORIGINAL");

// 1. Полноценный .sdmod
var a = MakeZip("a.sdmod", new() {
    ["mod.json"] = "{\"id\":\"mod-a\",\"name\":\"Mod A\"}",
    [F] = "A", ["RedirectorData/Characters_New/shirt.temp.bin"] = "" });
var ra = lib.Import(a);
Check(ra.Entry.Files.Count == 2 && ra.Warnings.Count == 0, "import .sdmod");

// 2. «Голый» zip без структуры -> Characters_New + предупреждение
var b = MakeZip("Mod B.zip", new() { ["shirt.perm.bin"] = "B" });
var rb = lib.Import(b);
Check(rb.Entry.Id == "mod-b" && rb.Entry.Files[0] == F, "bare zip mapped to Characters_New");
Check(rb.Warnings.Any(w => w.Contains(".temp.bin")), "warn missing temp.bin");

// 3. Zip-slip
var evil = MakeZip("evil.zip", new() { ["RedirectorData/../../evil.txt"] = "x" });
try { lib.Import(evil); Check(false, "zip-slip rejected"); } catch (InvalidDataException) { Check(true, "zip-slip rejected"); }

// 4. Дубликат без replace
try { lib.Import(a); Check(false, "duplicate"); } catch (ModExistsException) { Check(true, "duplicate"); }

// 5. Включение A: оригинал уходит в бэкап
Check(inst.Enable(ra.Entry).Ok && Read(F) == "A", "enable A");
Check(Exists("RedirectorData/Characters_New/shirt.temp.bin"), "temp.bin copied");

// 6. Конфликт с B
var conf = inst.Enable(rb.Entry);
Check(!conf.Ok && conf.Conflicts[0].OwnerModId == "mod-a" && Read(F) == "A", "conflict detected, nothing changed");
Check(inst.Enable(rb.Entry, true).Ok && Read(F) == "B", "enable B with override");

// 7. Выключаем A, пока B перекрывает: файл B остаётся
inst.Disable("mod-a");
Check(Read(F) == "B" && !Exists("RedirectorData/Characters_New/shirt.temp.bin"), "disable A keeps B's file, removes A's other files");

// 8. Выключаем B: возвращается ОРИГИНАЛ
inst.Disable("mod-b");
Check(Read(F) == "ORIGINAL" && state.Owners.Count == 0 && state.Backups.Count == 0, "disable B restores original");

// 9. Цепочка: A, затем B поверх; выключаем B -> снова A; выключаем A -> оригинал
inst.Enable(ra.Entry); inst.Enable(rb.Entry, true);
inst.Disable("mod-b");
Check(Read(F) == "A", "chain: B off -> A back");
inst.Disable("mod-a");
Check(Read(F) == "ORIGINAL" && !Exists("RedirectorData/Characters_New/shirt.temp.bin"), "chain: A off -> original");

// 10. Состояние переживает перезапуск
inst.Enable(ra.Entry);
var state2 = Json.LoadOrNew<AppState>(paths.StateFile);
Check(state2.EnabledMods.Contains("mod-a") && state2.Owners.Count == 2, "state persisted");
Check(inst.Verify(lib.LoadAll()).Count == 0, "verify ok");
File.Delete(Path.Combine(game, F.Replace('/', Path.DirectorySeparatorChar)));
Check(inst.Verify(lib.LoadAll()).Contains("mod-a"), "verify detects missing file");
inst.DisableAll();

// 11. Pack -> Import (круговой тест)
var src = Path.Combine(tmp, "src"); Directory.CreateDirectory(Path.Combine(src, "sub"));
File.WriteAllText(Path.Combine(src, "sub", "x.bin"), "X");
var packed = Path.Combine(tmp, "out.sdmod");
ModPackage.Pack(src, new ModManifest { Id = "Packed", Name = "Packed" }, packed);
var rp = lib.Import(packed);
Check(rp.Entry.Id == "packed" && rp.Entry.Files.SequenceEqual(new[] { "RedirectorData/Characters_New/sub/x.bin" }), "pack/import roundtrip");
lib.Export("packed", Path.Combine(tmp, "exp.sdmod"));
Check(File.Exists(Path.Combine(tmp, "exp.sdmod")), "export");

// 12. FileRedirector: офлайн-эвристика, установка, проверка
var cfg = new AppConfig(); var fake = MakeZip("fr.zip", new() { ["dinput8.dll"] = "dll", ["FileRedirector/readme.txt"] = "x" });
var bytes = File.ReadAllBytes(fake);
var game2 = Path.Combine(tmp, "game2"); Directory.CreateDirectory(game2);
var svcOffline = new RedirectorService(new AppConfig(), () => { }, _ => throw new HttpRequestException("offline"));
Check((await svcOffline.CheckAsync(game2)).State == RedirectorState.NotInstalled, "offline: not installed");
var svc = new RedirectorService(cfg, () => { }, _ => Task.FromResult(bytes));
Check(!(await svc.CheckAsync(game2)).IsOk, "online check: missing files");
await svc.InstallAsync(game2);
Check((await svc.CheckAsync(game2)).State == RedirectorState.Installed && Directory.Exists(Path.Combine(game2, "RedirectorData")), "install + detect");

// 13. Умное распознавание структуры архива
List<string> Map(Dictionary<string, string> files, out List<string> warns)
{
    var z = MakeZip("probe-" + Guid.NewGuid().ToString("N") + ".zip", files);
    using var za = ZipFile.OpenRead(z);
    var info = ModPackage.Inspect(za, "probe");
    warns = info.Warnings;
    return info.Files.Select(f => f.Rel).ToList();
}
const string CN = "RedirectorData/Characters_New/";
var m1 = Map(new() { ["Characters_New/a.perm.bin"] = "1", ["Characters_New/a.temp.bin"] = "" }, out var w1);
Check(m1.SequenceEqual(new[] { CN + "a.perm.bin", CN + "a.temp.bin" }) && !w1.Any(w => w.Contains("ignored") || w.Contains("temp.bin")), "Characters_New folder unwrapped");
var m2 = Map(new() { ["Character_New/x.bin"] = "1" }, out _);
Check(m2.SequenceEqual(new[] { CN + "x.bin" }), "typo Character_New accepted");
var m3 = Map(new() { ["MyMod/Characters_New/sub/x.bin"] = "1", ["MyMod/readme.txt"] = "r" }, out _);
Check(m3.SequenceEqual(new[] { CN + "sub/x.bin" }), "wrapper + Characters_New + subfolder");
var m4 = Map(new() { ["Wrapper/x.bin"] = "1", ["Wrapper/readme.txt"] = "r" }, out var w4);
Check(m4.SequenceEqual(new[] { CN + "x.bin" }) && w4.Any(w => w.Contains("Wrapper")), "single wrapper folder dropped, readme skipped");
var m5 = Map(new() { ["x.bin"] = "1", ["y.bin"] = "2" }, out var w5);
Check(m5.SequenceEqual(new[] { CN + "x.bin", CN + "y.bin" }) && !w5.Any(w => w.Contains("RedirectorData")), "bare files adapt silently");
var m6 = Map(new() { ["A/x.bin"] = "1", ["B/y.bin"] = "2" }, out _);
Check(m6.SequenceEqual(new[] { CN + "A/x.bin", CN + "B/y.bin" }), "several folders kept as is");
var m7 = Map(new() { ["RedirectorData/UI/i.bin"] = "1", ["__MACOSX/._x"] = "j", ["Thumbs.db"] = "t" }, out _);
Check(m7.SequenceEqual(new[] { "RedirectorData/UI/i.bin" }), "RedirectorData kept, junk skipped");
// Pack из папки с Characters_New
var src2 = Path.Combine(tmp, "src2"); Directory.CreateDirectory(Path.Combine(src2, "Characters_New"));
File.WriteAllText(Path.Combine(src2, "Characters_New", "z.bin"), "Z");
var packed2 = Path.Combine(tmp, "p2.sdmod"); ModPackage.Pack(src2, new ModManifest { Id = "p2", Name = "p2" }, packed2);
using (var zp = ZipFile.OpenRead(packed2)) Check(zp.Entries.Any(e => e.FullName == CN + "z.bin"), "pack unwraps Characters_New");

// 14. Выбор exe: debug не выбирается автоматически
var g3 = Path.Combine(tmp, "game3"); Directory.CreateDirectory(g3);
foreach (var n in new[] { "SDHDShip.exe", "SDHDShip_debug.exe", "unins000.exe" }) File.WriteAllText(Path.Combine(g3, n), "x");
Check(Path.GetFileName(GameLocator.FindExecutable(g3)) == "SDHDShip.exe", "auto exe skips debug");
Check(GameLocator.ListExecutables(g3).Count == 2, "exe list excludes uninstaller");

// 15. LZMA-архивы (реальный zip, созданный 7-Zip: именно так упакован FileRedirector)
string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();
var lzmaZip = Path.Combine(AppContext.BaseDirectory, "Fixtures", "lzma.zip");
var expected = new Dictionary<string, string>
{
    ["RedirectorData/Characters_New/text.perm.bin"] = "a98cf527d7b7641412dbb9f18710ac184ab69da49201b9424b44d244a78d9042",
    ["RedirectorData/Characters_New/random.temp.bin"] = "b3a8a9294ae5f20bdf50cfdb5a6149ae0b78e64dd90f0f8edd8a5d024d061d59",
    ["RedirectorData/Characters_New/mixed.bin"] = "fa5d17db150f2ece1833e0622e4cbf8c039361d52c5bbfe5e0289a4ac9077a4f",
    ["RedirectorData/Characters_New/tiny.bin"] = "2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881",
    ["RedirectorData/Characters_New/empty.bin"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
};
// 15a. встроенный ZipArchive действительно не умеет LZMA (проверяем, что фикстура правильная)
using (var zz = ZipFile.OpenRead(lzmaZip))
{
    var e = zz.GetEntry("RedirectorData/Characters_New/text.perm.bin")!;
    try { using var _ = e.Open(); Check(false, "fixture must be LZMA"); } catch (InvalidDataException) { Check(true, "fixture is LZMA"); }
}
// 15b. наш ридер распаковывает все записи побитово верно
using (var fsz = File.OpenRead(lzmaZip))
using (var zr = new ZipFileReader(fsz))
{
    var outDir = Path.Combine(tmp, "lzma-out");
    foreach (var (name, hash) in expected)
    {
        var dest = Path.Combine(outDir, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        zr.ExtractToFile(zr.GetEntry(name)!, dest);
        Check(Sha(File.ReadAllBytes(dest)) == hash, "LZMA extract " + name);
    }
    Check(zr.ReadAllBytes(zr.GetEntry("mod.json")!).Length > 0, "LZMA/stored ReadAllBytes");
}
// 15c. импорт мода, упакованного в LZMA, и PeekId
var rl = lib.Import(lzmaZip);
Check(rl.Entry.Id == "lzma-mod" && rl.Entry.Files.Count == 5 && lib.PeekId(lzmaZip) == "lzma-mod", "import LZMA mod");
var importedMixed = Path.Combine(rl.Entry.FilesRoot, "RedirectorData", "Characters_New", "mixed.bin");
Check(Sha(File.ReadAllBytes(importedMixed)) == expected["RedirectorData/Characters_New/mixed.bin"], "imported LZMA content intact");
// 15d. установка FileRedirector из LZMA-архива
var game4 = Path.Combine(tmp, "game4"); Directory.CreateDirectory(game4);
var svcLzma = new RedirectorService(new AppConfig(), () => { }, _ => Task.FromResult(File.ReadAllBytes(lzmaZip)));
await svcLzma.InstallAsync(game4);
Check(File.Exists(Path.Combine(game4, "RedirectorData", "Characters_New", "text.perm.bin")) &&
      Sha(File.ReadAllBytes(Path.Combine(game4, "RedirectorData", "Characters_New", "mixed.bin"))) == expected["RedirectorData/Characters_New/mixed.bin"], "FileRedirector install from LZMA zip");
// 15e. повреждённый архив даёт ошибку, а не мусор
var brokenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "broken.zip");
try
{
    using var bf = File.OpenRead(brokenPath); using var bz = new ZipFileReader(bf);
    bz.ExtractToFile(bz.GetEntry("RedirectorData/Characters_New/text.perm.bin")!, Path.Combine(tmp, "broken.out"));
    Check(false, "corrupt LZMA detected");
}
catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { Check(true, "corrupt LZMA detected"); }

Directory.Delete(tmp, true);
Console.WriteLine($"Passed: {passed}, Failed: {failed}");
return failed == 0 ? 0 : 1;
