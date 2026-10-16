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

**Elevation:** Uygulama asla bütünüyle admin çalışmaz. System kapsamlı değişiklik gerekiyorsa plan JSON olarak yazılır ve aynı exe `envkit apply-plan <dosya>` ile `runas` üzerinden kısa ömürlü elevated process olarak çalıştırılır; yalnızca o planı uygulayıp çıkar.

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
7. **Snapshot'lar** `%LOCALAPPDATA%\EnvKit\snapshots\` altında JSON; env değerleri de secret içerebileceğinden DPAPI ile şifreli.

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
- `ChangePlan { Id, Operations[], RequiresElevation }`
  - Operasyonlar: `SetVar`, `DeleteVar`, `PathInsert`, `PathRemove`, `PathMove`, `CredWrite`, `CredDelete`, `DotEnvSet`, `DotEnvDelete`, `DotEnvRename`
- `Snapshot { Id, CreatedAt, Reason, PlanId, State }`
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
envkit profile apply <name>               (v0.3)
envkit run --profile <name> -- <cmd>      (v0.3)
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
