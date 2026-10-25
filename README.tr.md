# ValoTools

[English](README.md) · **Türkçe**

ValoTools, C# ve .NET 10 WPF ile geliştirilmiş VALORANT temalı bir masaüstü uygulamasıdır. Oynanabilir ajanları [Valorant API](https://valorant-api.com/v1/agents?isPlayableCharacter=true) üzerinden alır. Riot Client ve VALORANT açıkken bölgeyi yerel oturum bilgilerinden otomatik belirler ve ajan seçimi ekranında seçtiğiniz ajanı kilitlemeyi dener.

Ajanları role göre filtreleyebilir veya isme göre arayabilirsiniz. Sol alttaki bağlantı kartı, VALORANT oturumu algılandığında Riot adınızı ve etiketinizi gösterir. Düzenlenebilir logo kaynağı `assets/ValoTools.svg`, uygulama ikonu `assets/ValoTools.ico` konumundadır.

Arayüzde gövde metinleri için Figtree, başlıklar için Barlow Condensed kullanılır. İki font da uygulamaya gömülüdür; lisans metinleri `assets/fonts` klasöründedir.

## Kaynak koddan çalıştırma

Windows ve .NET 10 SDK gerekir.

```powershell
git clone https://github.com/Aronshire/ValoTools.git
cd ValoTools
dotnet run --project ValoTools.csproj
```

Bir ajan seçip **İzlemeyi Başlat** düğmesine basın. Bölgeyi elle seçmeniz gerekmez. Uygulama önce VALORANT oturumunun GLZ adresini, ardından Riot Client bölge bilgisini, son olarak güncel ShooterGame günlüğünü kontrol eder. Bölge güvenilir biçimde belirlenemezse varsayılan bir bölge seçmek yerine hata gösterir.

Derlenmiş uygulama `dist\ValoTools.exe` konumundadır. Bu sürümün açılması için bilgisayarda .NET 10 Desktop Runtime bulunmalıdır; `dist` klasöründeki diğer dosyaları da yanında tutun. ValoTools, eski ValoInsta ayarlarını ilk açılışta okur.

## Windows kurulumu

`dist\ValoTools-Setup-1.2.2.exe`, x64 Windows 10/11 için kurulum dosyasıdır. .NET 10 Desktop Runtime pakete dahildir. Yönetici izni istemeden mevcut kullanıcı hesabına kurulur, Başlat menüsüne kısayol ekler, isteğe bağlı masaüstü kısayolu sunar ve Windows'un Uygulamalar bölümünden kaldırılabilir. Aynı uygulama kimliğini kullanarak eski ValoInsta kurulumunu da günceller.

Kurulu uygulama açılışta ve açık kaldığı sürece 30 dakikada bir [GitHub Releases](https://github.com/Aronshire/ValoTools/releases/latest) üzerinden güncellemeleri denetler. Yan menüde mevcut sürüm ve **Güncellemeyi kontrol et** düğmesi görünür. Yeni sürüm varsa `ValoTools-Setup-<sürüm>.exe` dosyasını arka planda indirir, GitHub'ın yayımladığı SHA-256 özetiyle doğrular ve **Güncellemeyi kur** düğmesini gösterir. Kurulum bu düğmeye bastığınızda başlar. Kaynak koddan çalıştırılan sürüm otomatik denetim yapmaz; elle kontrol düğmesini kullanabilirsiniz.

Kurulum dosyasını yeniden oluşturmak için .NET 10 SDK ve Inno Setup 7 kurup şu komutu çalıştırın:

```powershell
./installer/build.ps1
```

Kurulum betiği `installer/ValoTools.iss` konumundadır. Mevcut dağıtımda kod imzalama sertifikası kullanılmadığı için Windows yayıncıyı doğrulayamaz.

Mevcut imza durumu ve planlanan süreç [kod imzalama politikasında](CODE_SIGNING_POLICY.md), uygulamanın kullandığı yerel veriler ve ağ bağlantıları ise [gizlilik politikasında](PRIVACY.md) açıklanır.

## Yeni sürüm yayımlama

`ValoTools.csproj` içindeki `Version` ile `installer/ValoTools.iss` içindeki `AppVersion` aynı olmalıdır. Değişiklikleri GitHub'a gönderin, ardından aynı commit'i gösteren bir `vX.Y.Z` etiketi gönderin. `.github/workflows/release.yml`, Windows kurulumunu derleyip GitHub Release varlığı olarak yayımlar. Kurulu istemciler bir sonraki güncelleme denetiminde yeni sürümü indirir. Kurulumu yerelde `./installer/build.ps1` ile de oluşturabilirsiniz.

## Doğrulama

```powershell
dotnet build ValoTools.csproj
dotnet run --project tests\RegionResolverCheck\RegionResolverCheck.csproj
dotnet run --project tests\UpdateCheck\UpdateCheck.csproj
```

Bu uygulama resmî olmayan oyun istemcisi uç noktalarını kullanır. Gerçek ajan kilitleme akışı, açık bir VALORANT oturumu ve maçla test edilmelidir. [Valorant.js](https://github.com/aronshire/Valorant.js) projesindeki lockfile, token ve pregame yöntemleri referans alınmıştır; ValoTools o projedeki sabit istemci sürümüne bağımlı değildir.

## Lisans

ValoTools kaynak kodu [MIT Lisansı](LICENSE) ile yayımlanır. Uygulamaya gömülü Figtree ve Barlow Condensed fontları kendi SIL Open Font License 1.1 koşullarına tabidir; metinleri `assets/fonts` klasöründedir.
