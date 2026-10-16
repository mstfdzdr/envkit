# EnvKit — Tasarım Dokümanı

> Durum: Onaylandı (2026-10-16). CLI adı: `envkit`. Lisans: MIT.

## Problem

Windows'ta ortam değişkenleri, PATH ve credential'lar birbirinden kopuk, eski ve kırılgan araçlarla yönetiliyor:

- **PATH editörü** küçük; ölü klasörleri, tekrarları ve User/System çakışmalarını göstermiyor; geri alma yok.
- **Ortam değişkenleri** değişince açık terminaller görmüyor, System tarafı admin istiyor, geçmiş/yedek yok.
- **Credential Manager**'da arama/filtre zayıf, toplu silme yok, hangi kaydın hangi araca ait olduğu belirsiz.
- **Proje secret'ları** (`.env`, `dotnet user-secrets`) her projede dağınık durumda.

## Hedef kullanıcı

Windows'ta çalışan geliştiriciler: birden fazla SDK/runtime sürümüyle uğraşanlar, git/Azure/npm token'larını yönetenler. Açık kaynak; winget ile dağıtılır.

## Temel ilke: her değişiklik bir *Plan*

Registry'e, Credential Manager'a, kasaya veya proje dosyalarına giden **her** yazma işlemi aynı yoldan geçer:

```
 Değişiklik isteği ──► ChangePlan ──► Diff önizleme ──► Snapshot ──► Uygula ──► Yayın (WM_SETTINGCHANGE)
 (GUI/CLI/Profil)      (saf veri)     (kullanıcı onayı)  (otomatik)   (gerekirse elevated)
```

Kazanımlar: CLI'de `--dry-run` bedava, her zaman geri alınabilirlik, elevation tek noktada, plan saf veri olduğu için kolay test.

## Sürüm planı

| Sürüm | İçerik |
|---|---|
| **v0.1 — Env & PATH** | User/System değişken editörü, PATH liste editörü (sürükle-bırak), sağlık kontrolü, otomatik snapshot + diff + geri yükleme, temel CLI, CI + installer + winget |
| **v0.2 — Credential Manager** | Generic credential listeleme/arama, araca göre gruplama (git, Azure, VS, npm…), düzenleme, toplu silme, secret'sız export |
| **v0.3 — Profiller + Kasa** | Profiller (örn. "Java 17" → `JAVA_HOME` + PATH), DPAPI şifreli secret kasası, `envkit run` ile process'e secret enjeksiyonu |
| **v0.4 — Proje secret'ları** | `.env` ve `dotnet user-secrets` tarama, **görüntüleme ve düzenleme**, kasadaki değerlere referans |

**Kapsam dışı:** uzak makine yönetimi, bulut senkronizasyonu, Linux/macOS, domain credential parolalarını okumak (Windows izin vermez).

## Mimari

```
 ┌──────────────┐   ┌──────────────┐
 │ EnvKit.App   │   │ EnvKit.Cli   │   ince katmanlar (UI / komut ayrıştırma)
 │ (WPF, MVVM)  │   │ (System.     │
 │              │   │  CommandLine)│
 └──────┬───────┘   └──────┬───────┘
        └────────┬─────────┘
          ┌──────▼───────┐
          │ EnvKit.Core  │   PATH analizi, ChangePlan, profil motoru, snapshot servisi,
          │ (saf .NET)   │   .env parser, arayüzler (IEnvStore, ICredStore, IVault)
          └──────┬───────┘
          ┌──────▼────────┐
          │ EnvKit.Windows│  Registry, CredEnumerate/CredWrite P/Invoke, DPAPI,
          │               │  SendMessageTimeout yayını, elevation
          └───────────────┘
```

**Elevation:** Uygulama asla bütünüyle admin çalışmaz. System kapsamlı değişiklik gerekiyorsa plan JSON olarak yazılır ve aynı exe `apply-plan <dosya> --sha256 <hash>` ile `runas` üzerinden kısa ömürlü elevated process olarak çalıştırılır; yalnızca o planı uygulayıp çıkar. Ayrıntılar (`PlanExecutor`, `WindowsElevation`, `ElevatedPlanHost`):

- Elevated process **yalnızca Machine** değişikliklerini uygular; User değişikliklerini normal process yazar. (UAC'de başka bir admin hesabı kullanılırsa elevated process'in HKCU'su o admin'e aittir.)
- Sıra: ön koşul kontrolü (UAC'den önce) → snapshot (normal process; DPAPI anahtarı kullanıcınınki) → User değişiklikleri → UAC + Machine değişiklikleri. UAC reddedilir veya helper başarısız olursa User değişiklikleri admin gerekmeden geri alınır.
- Plan dosyası `%LOCALAPPDATA%\EnvKit\plans` altında; helper onu bir kez belleğe okur ve komut satırındaki SHA-256 ile doğrular (UAC onayı ile okuma arasında dosyanın değiştirilmesine karşı).
- Helper kullanıcı klasörüne hiçbir şey yazmaz; sonucu yalnızca çıkış koduyla bildirir (symlink/junction ile ayrıcalık yükseltmeye karşı).

## Kritik teknik detaylar

1. **`REG_EXPAND_SZ` koruması.** `Environment.SetEnvironmentVariable(..., Machine/User)` değeri `REG_SZ` yazar ve `%JAVA_HOME%\bin` gibi referansları bozar. Registry API doğrudan kullanılır, orijinal değer tipi korunur.
2. **PATH sağlık kontrolü:**
   - Var olmayan klasörler (`%VAR%` expand edildikten sonra kontrol)
   - Tekrarlar (büyük/küçük harf ve sondaki `\` normalize edilerek)
   - Boş girdiler, gereksiz tırnaklar
   - **Gölgeleme:** System PATH, User PATH'ten önce gelir. `envkit which python` hangi `python.exe`'nin kazandığını ve gölgede kalanları gösterir (WindowsApps stub'ları dahil).
3. **Yayın:** `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, "Environment")`. Açık terminaller yine de görmez; UI bunu açıkça belirtir.
4. **Limitler:** tek değişken ≤ 32.767 karakter; credential blob ≤ 2.560 byte. `setx` (1.024'te keser) kullanılmaz.
5. **Credential güvenliği:** parolalar varsayılan gizli; "göster" için isteğe bağlı Windows Hello (`UserConsentVerifier`). Export'lara secret yazılmaz.
6. **Kasa:** DPAPI (CurrentUser); ileride isteğe bağlı master parola (Argon2id + AES-GCM). Secret'lar kalıcı env'e **yazılmaz**; `envkit run --profile api -- dotnet run` ile yalnızca o process'e enjekte edilir.
7. **Snapshot'lar** `%LOCALAPPDATA%\EnvKit.Data\snapshots\` altında JSON; env değerleri de secret içerebileceğinden DPAPI ile şifreli. Kurulum klasöründen (`%LOCALAPPDATA%\EnvKit`, kaldırmada silinir) bilinçli olarak ayrı tutulur ki geri alma geçmişi kaldırmadan sonra da kalsın.
8. **Secret maskeleme:** Adı veya değeri credential'a benzeyen değişkenler (`*_TOKEN`, `*_API_KEY`, `*_PAT`, `ghp_…` vb.) GUI'de ve `envkit var list`'te maskelenir; açıkça "göster" denmeden görünmez, aranmaz, düzenlenemez.
9. **Paketleme:** Velopack ile per-user kurulum. `current\` altında masaüstü uygulaması, `current\bin\envkit.exe` tek dosyalık CLI. Kurulum hook'u yalnızca `bin` klasörünü User PATH'e ekler (runtime DLL'leri PATH'e girmez), kaldırma hook'u çıkarır; ikisi de snapshot'lı bir plandır.

## Credential Manager (v0.2) — uygulanan kararlar

- **Model:** Kimlik `CredentialKey { TargetName, Kind }` (Windows bir kaydı hedef adı + tür ile tanır; hedef adı büyük/küçük harfe duyarsız). `CredentialState` kullanıcı adı, secret byte'ları, persistence, yorum, alias ve attribute'ları taşır; `ToString` secret'ı asla yazmaz.
- **Plan entegrasyonu:** `CredentialChange { Key, Before, After }` değişkenlerle aynı `PlanChange` tabanından türer; çakışma kontrolü, hata durumunda geri alma ve inceleme ekranı ortaktır. Credential Manager kullanıcıya özel olduğu için hiçbir zaman elevated helper'a gitmez.
- **Snapshot'ta secret:** Bir plan credential değiştirdiğinde, snapshot **yalnızca o planın dokunduğu** credential'ların önceki durumunu (secret dahil) saklar; bütün kasa kopyalanmaz. Snapshot zaten DPAPI ile şifreli. Böylece toplu silme ve düzenleme geri alınabilir.
- **Domain credential'lar:** Windows bunların secret'ını (domain parolası, RDP vb.) hiçbir uygulamaya vermez. Listelenir ve silinebilir ama düzenlenemez; silme işlemi geri alınamaz ve inceleme ekranı bunu açıkça uyarır. Restore bunları atlar.
- **Kodlama:** Secret bir kodlama işareti taşımaz. Araçların çoğu UTF-16, bazıları (git, Docker) UTF-8 yazar. Kodlama sezgisel olarak tespit edilir ve düzenleme sırasında korunur, böylece sahibi olan araç kaydı okumaya devam eder.
- **Gruplama:** Hedef adı kalıplarına göre: `git:` → Git, `GitHub - <url>` → GitHub Desktop, `vscode…` → VS Code, `TERMSRV/` → Remote Desktop, domain türleri → Network vb. Eşleşmeyenler "Other".
- **Parola girişi:** CLI'da parola asla argüman olarak alınmaz (shell geçmişine düşer); etkileşimli terminalde yankısız sorulur ya da `--password-stdin` ile okunur. Boş giriş mevcut parolayı korur.
- **Export:** Yalnızca envanter (hedef, tür, grup, kullanıcı, persistence, yorum, tarih); secret alanı yoktur.
- **Teknik not:** `CRED_ENUMERATE_ALL_CREDENTIALS` hedef adlarını `LegacyGeneric:target=…` biçiminde döndürür ve bu adlar `CredRead`/`CredDelete` ile kullanılamaz; bu yüzden filtresiz listeleme bayraksız yapılır.
- **Ertelenen:** "Göster" için Windows Hello onayı (WPF'te `IUserConsentVerifierInterop` gerektiriyor). Şimdilik secret yalnızca açık bir tıklamayla gösterilir ve seçim değişince gizlenir.

## Profiller ve kasa (v0.3) — uygulanan kararlar

- **Profil:** `Profile { Name, Description, Actions[] }`; eylemler `set`, `unset`, `prependPath`, `appendPath`, `removePath`. `%LOCALAPPDATA%\EnvKit.Data\profiles\<ad>.json` altında, elle yazılabilir JSON (eylem türü nesnenin herhangi bir yerinde olabilir). Secret içermez; paylaşılabilir.
  ```json
  { "name": "java21", "actions": [
      { "action": "set", "name": "JAVA_HOME", "value": "C:\\jdk-21" },
      { "action": "prependPath", "directory": "%JAVA_HOME%\\bin" } ] }
  ```
- **Kalıcı uygulama** (`profile apply`): yalnızca **User** kapsamına, sıradan bir plan olarak (diff, onay, snapshot, geri alma). `%VAR%` referansları korunur (REG_EXPAND_SZ), bu yüzden `%JAVA_HOME%\bin` kalıbıyla JDK geçişi yalnızca `JAVA_HOME`'u değiştirir. `prependPath` mevcut girdiyi başa taşır.
- **Kasa:** `vault.dat`, tek DPAPI şifreli dosya. Çözülemeyen bir kasa (başka kullanıcı, kopyalanmış profil) boş sayılmaz, hata verir; aksi halde sonraki `set` tüm secret'ların üzerine yazardı. Düz metin tamponları sıfırlanır.
- **Referans:** Profil değerlerinde `${vault:NAME}`. Kasa referanslı profiller kalıcı olarak **uygulanamaz**; yalnızca `envkit run` ile kullanılabilir.
- **`envkit run -p <profil>... -s NAME[=SECRET]... -- <komut>`:** Mevcut process ortamından başlar, profilleri sırayla uygular; her değer önce o ana kadar kurulan ortama göre expand edilir, sonra kasa referansları doldurulur (secret içindeki `%` asla expand edilmez). Komut yeni PATH'te aranır; alt process'in çıkış kodu aynen döner; Ctrl+C alt process'e bırakılır. Kalıcı ortama hiçbir şey yazılmaz.
- **Kasa ve plan:** Kasa işlemleri plan/snapshot yolundan geçmez (secret'ları snapshot'a kopyalamamak için). Bu yüzden kasa silme geri alınamaz; CLI onay ister, GUI iki tıklama ister.
- **Stdin:** Windows PowerShell pipe ile gönderdiği metnin başına UTF-8 BOM koyar ve `Console.In` bunu ayıklamaz. `--value-stdin` / `--password-stdin` okumaları bu karakteri temizler (aksi halde secret'ın parçası oluyordu).

## `.env` ve user-secrets düzenleme (v0.4)

Düzenleme, dosyanın geri kalanına dokunmadan yapılmalı:

- **Round-trip parser:** dosya satır token'larına ayrılır (yorum, boş satır, `KEY=value`, `export KEY=value`). Tırnak stili (`"`, `'`, yok), satır içi yorumlar, sıra, encoding (BOM dahil) ve satır sonları (CRLF/LF) korunur. Değiştirilmeyen satırlar byte-byte aynı kalır.
- **Operasyonlar:** `DotEnvSet`, `DotEnvDelete`, `DotEnvRename` — diğer her şey gibi ChangePlan üzerinden.
- **Çakışma tespiti:** plan oluşturulurken dosya hash'i alınır; uygulama anında dosya değişmişse işlem durur ve kullanıcıya diff gösterilir.
- **Atomik yazma:** temp dosyaya yaz → `File.Replace`. Yazmadan önce dosya snapshot'ı alınır.
- **user-secrets:** `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`, `System.Text.Json` node API'siyle düzenlenir; `UserSecretsId` csproj'dan bulunur.
- **Uyarılar:** `.env` dosyası `.gitignore` kapsamında değilse veya git'e commit edilmişse uyarı gösterilir.
- **Kasa referansı:** `.env` içindeki değer kasadaki bir anahtara bağlanabilir; `envkit run` sırasında çözülür.

## Veri modeli (özet)

- `EnvVariable { Name, Value, Scope(User|Machine), Kind(Literal|Expandable) }`
- `PathEntry { Raw, Expanded, Directory, Scope, Index }` + `PathReport { Entries, Issues[] }`
  - Sorun türleri: `Empty`, `Missing`, `Duplicate`, `Quoted`, `UnresolvedVariable`, `NotExpanded` (REG_SZ PATH içinde `%VAR%`), `NotFullyQualified`, `ValueTooLong`
  - Makine değişkenleri kullanıcı değişkenlerinden önce expand edilir; System PATH kullanıcıya özel bir değişkeni referanslayamaz.
- `ChangePlan { Id, Description, CreatedAt, Changes[], RequiresElevation }`
  - Her değişiklik **önce/sonra** durumu olarak tutulur (`VariableChange { Scope, Name, Before, After }`; `Before = null` oluşturma, `After = null` silme). Plan aynı zamanda diff'tir.
  - `Before`, uygulama anında ön koşul olarak tekrar kontrol edilir; arada değişen bir değer varsa `PlanConflictException`.
  - PATH ekleme/çıkarma/sıralama `PlanBuilder` yardımcılarıdır, ayrı operasyon türü değildir. İleride `CredentialChange`, `DotEnvChange` aynı `PlanChange` tabanından türeyecek.
  - Uygulama sırasında bir yazma başarısız olursa önceki yazmalar geri alınır.
- `Snapshot { Info { Id, CreatedAt, Reason, PlanId }, Variables[] }` — her iki kapsamın tamamı; restore, snapshot'tan üretilen sıradan bir plandır.
- `Profile { Name, Operations[] }` — kaydedilmiş ChangePlan şablonu
- `VaultSecret { Key, ProtectedValue, Tags[] }`

## CLI taslağı

```
envkit path list [--scope user|machine] [--check]
envkit path add <dir> [--scope user] [--prepend] [--dry-run]
envkit path fix --dedupe --remove-missing
envkit which <exe>
envkit var get|set|unset <name> [--scope ...]
envkit snapshot list | diff <id> | restore <id>
envkit profile new|list|show|apply|delete|import|export   (v0.3)
envkit vault list|set|get|delete                          (v0.3)
envkit run -p <profile> -s NAME[=SECRET] -- <cmd>         (v0.3)
envkit cred list [--filter git:*]         (v0.2)
envkit dotenv get|set|unset <key> [--file .env]   (v0.4)
```

## Ekranlar

Sol navigasyon: **Ortam Değişkenleri** · **PATH** (sağlık rozetleri) · **Credential'lar** · **Kasa** · **Profiller** · **Proje Secret'ları** · **Geçmiş** (snapshot + diff). Her kaydetmeden önce "Değişiklikleri incele" paneli.

## Teknoloji

| Alan | Seçim | Neden |
|---|---|---|
| Runtime | .NET 10 (LTS), `net10.0-windows10.0.19041.0` | Uzun destek; WinRT (Windows Hello) erişimi |
| UI | WPF + CommunityToolkit.Mvvm + WPF-UI (Fluent) | Olgun, Win32'ye yakın, modern görünüm |
| CLI | System.CommandLine | Standart, tab-completion |
| Credential | Kendi ince P/Invoke katmanımız | Bağımlılık yok, tam kontrol |
| Paketleme | Velopack + winget manifest | Installer + otomatik güncelleme |
| İmzalama | SignPath Foundation | OSS'ye ücretsiz; SmartScreen |
| Test | xUnit + sahte `IEnvStore` | Gerçek registry'ye dokunmadan test |
| CI | GitHub Actions | Build, test, release |

## Uygulama sırası (v0.1)

1. Solution iskeleti, Core modelleri, `IEnvStore`
2. Registry implementasyonu (tip korumalı) + WM_SETTINGCHANGE yayını
3. PATH analizörü + testler
4. ChangePlan + snapshot + restore
5. CLI komutları
6. WPF ekranları: Değişkenler, PATH, Geçmiş
7. Elevated `apply-plan` akışı
8. CI, Velopack, winget
