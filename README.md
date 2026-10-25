# ValoTools

VALORANT temalı, C#/.NET 10 WPF masaüstü araç uygulaması. İlk aracı, ajanları [Valorant API](https://valorant-api.com/v1/agents?isPlayableCharacter=true) adresinden alır. Riot Client ve VALORANT açıkken bölgeyi yerel oturum bilgilerinden otomatik bulur; ajan seçimi ekranında seçilen ajanı kilitlemeyi dener.

Ajan listesi rol sekmeleriyle ve aramayla birlikte filtrelenir. Sol alttaki bağlantı kartı VALORANT oturumu algılandığında Riot adını ve etiketini gösterir. Uygulama logosunun düzenlenebilir kaynağı `assets/ValoTools.svg`, ikon dosyası `assets/ValoTools.ico` konumundadır.

Arayüzde Elowell'deki Figtree metin fontu ve Barlow Condensed başlık fontu kullanılır. Fontlar uygulamaya gömülüdür; lisans metinleri `assets/fonts` altındadır.

## Çalıştırma

Windows ve .NET 10 SDK gerekir.

```powershell
cd C:\Projects\ValoInsta
dotnet run --project ValoTools.csproj
```

Uygulamada bir ajan seçin ve **İzlemeyi Başlat** düğmesine basın. Bölge seçimi yoktur. Uygulama önce VALORANT oturumunun GLZ adresini, sonra Riot Client bölge bilgisini, son olarak güncel ShooterGame günlüğünü kullanır. Bir bölge güvenle belirlenemezse hata gösterir; varsayılan bölgeye sessizce düşmez.

Derlenmiş uygulama `dist\ValoTools.exe` konumundadır. Bu sürümün açılması için bilgisayarda .NET 10 Desktop Runtime bulunmalıdır; `dist` klasöründeki dosyaları birlikte tutun. Eski ValoInsta ayarı yeni uygulamada ilk açılışta okunur.

## Windows kurulumu

`dist\ValoTools-Setup-1.2.0.exe` x64 Windows 10/11 için kurulum dosyasıdır. .NET 10 masaüstü çalışma zamanı pakete dahildir. Yönetici izni istemeden kullanıcı hesabına kurulur; Başlat menüsüne kısayol ekler, isteğe bağlı masaüstü kısayolu sunar ve Windows'un Uygulamalar bölümünden kaldırılabilir. Önceki ValoInsta kurulumunu aynı uygulama kimliğiyle günceller.

Kurulu uygulama açılışta ve açık kaldığı sürece 30 dakikada bir [GitHub Releases](https://github.com/Aronshire/ValoTools/releases/latest) üzerinden yeni sürümü denetler. Yeni sürüm varsa `ValoTools-Setup-<sürüm>.exe` dosyasını arka planda indirir, GitHub'ın yayınladığı SHA-256 özetiyle doğrular ve sol tarafta **Güncellemeyi kur** düğmesini gösterir. Kurulum kullanıcı düğmeye bastığında başlar. Kaynak koddan çalıştırılan sürüm güncelleme denetimi yapmaz.

Kurulum dosyasını yeniden oluşturmak için .NET 10 SDK ve Inno Setup 7 gerekir:

```powershell
./installer/build.ps1
```

Kurulum tarifi `installer/ValoTools.iss` dosyasındadır. Dağıtım için kod imzalama sertifikası kullanılmadığından Windows yayıncıyı doğrulayamaz.

## Yeni sürüm yayımlama

`ValoTools.csproj` içindeki `Version` ile `installer/ValoTools.iss` içindeki `AppVersion` aynı olmalıdır. Değişiklikleri GitHub'a gönderin, sonra aynı commit için `vX.Y.Z` etiketini gönderin. `.github/workflows/release.yml` Windows kurulumunu derleyip GitHub Release ve kurulum varlığını otomatik yayımlar. Kurulu istemciler bir sonraki açılışta sürümü otomatik indirir. İsterseniz aynı kurulumu yerelde `./installer/build.ps1` ile oluşturabilirsiniz.

## Doğrulama

```powershell
dotnet build ValoTools.csproj
dotnet run --project tests\RegionResolverCheck\RegionResolverCheck.csproj
dotnet run --project tests\UpdateCheck\UpdateCheck.csproj
```

Bu uygulama resmî olmayan oyun istemcisi uç noktalarını kullanır. Gerçek ajan kilitleme akışı açık VALORANT oturumu ve maçla test edilmelidir. Kaynak `C:\Projects\Valorant.js` projesindeki lockfile, token ve pregame yöntemleri referans alınmıştır; o projedeki sabit istemci sürümüne bağımlı değildir.
