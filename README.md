# Sigorta Takip

Şehirlerarası otobüslerin zorunlu trafik sigortası, kasko ve koltuk sigortası bitiş tarihlerini takip eden web uygulaması. React + TypeScript arayüz ve ASP.NET Core (.NET 10) API'den oluşur.

## Özellikler

- Araç ekleme, düzenleme, silme ve gelişmiş filtreleme/sayfalama
- Zorunlu trafik, kasko ve koltuk sigortası başlangıç/bitiş tarihi takibi
- Bugün biten, günü geçmiş ve 15 gün içinde bitecek poliçeleri dashboard üzerinde vurgulama
- SMTP ayarı yapıldığında **kademeli** otomatik e-posta hatırlatmaları (ör. 15/7/1 gün kala ve bitiş günü)
- Manuel e-posta kontrol butonu
- Yönetici (superadmin) ve Gözlemci (viewer) rolleri; rol atama/yükseltme/düşürme
- Yönetici girişi, şifre değiştirme ve e-posta ile şifre sıfırlama
- Excel (.xlsx) içe/dışa aktarma, JSON yedek alma ve geri yükleme

## Kurulum

1. Bağımlılıkları yükleyin: `npm install`
2. `.env.example` dosyasını `.env` olarak kopyalayıp değerleri doldurun.

```bash
cp .env.example .env
```

## Ortam Değişkenleri

| Değişken | Zorunlu | Açıklama |
| --- | --- | --- |
| `SUPERADMIN_EMAIL` | Üretimde **evet** | İlk açılışta oluşturulan ana yönetici e-postası |
| `SUPERADMIN_PASSWORD` | Üretimde **evet** | Ana yönetici şifresi (üretimde eksikse sunucu başlamaz) |
| `DATA_DIR` | Hayır | Kalıcı veri klasörü (db.json, sessions.json, şifreleme anahtarları). Varsayılan `../data` |
| `APP_TIMEZONE` | Hayır | İş saat dilimi (IANA). Varsayılan `Europe/Istanbul` |
| `REMINDER_DAYS` | Hayır | Hatırlatma günleri (virgülle). Varsayılan `15,7,1,0` (0 her zaman dahildir) |
| `ALLOWED_ORIGINS` | Hayır | Ek CORS kaynakları. Aynı origin sunumda gerekmez |
| `PORT` | Hayır | Sunucu portu (bulut sağlayıcılar genelde otomatik atar) |

## Çalıştırma

```bash
npm run dev
```

- Frontend: `http://127.0.0.1:5173/`
- Backend API: `http://127.0.0.1:5001/`

Production build:

```bash
npm run build
npm run start
```

## Test ve Lint

```bash
npm test    # node yerleşik test çalıştırıcısı (ek bağımlılık yok)
npm run lint
```

## E-posta Bildirimleri

`Ayarlar > E-posta Bildirim Ayarları` bölümünden SMTP bilgilerini girip bildirimleri etkinleştirin. Sistem açılışta, her gün 08:00'de (iş saat dilimi) ve gün içinde periyodik olarak poliçeleri kontrol eder ve `REMINDER_DAYS` eşiklerine ulaşan poliçeler için uyarı gönderir. Aynı eşik için tekrar e-posta göndermez; sunucu bir eşik gününde kapalı kaldıysa açıldığında eksik bildirimi telafi eder.

> **Port notu:** Yerleşik .NET e-posta istemcisi yalnızca STARTTLS'i destekler. Gmail/Outlook için **587** portunu kullanın. Port 465 (örtük SSL) desteklenmez.

Gmail kullanıyorsanız normal hesap şifresi yerine **uygulama şifresi** gerekir.

## Veri ve Kalıcılık

Tüm veriler `DATA_DIR` (varsayılan `data/`) altında `db.json` dosyasında tutulur; oturumlar `sessions.json`, SMTP şifresi ise şifrelenerek saklanır.

> **Bulut dağıtımı (Render vb.):** Konteyner dosya sistemi geçicidir. Kalıcı bir disk bağlayıp `DATA_DIR`'i o bağlama noktasına ayarlamazsanız her yeniden dağıtımda tüm veriler (araçlar, kullanıcılar, ayarlar) silinir. Dockerfile `/app/data` için bir `VOLUME` tanımlar.

Arayüzdeki `Yedek İndir` butonu ile JSON yedeği alabilirsiniz. Güvenlik nedeniyle SMTP şifresi yedeğe dahil edilmez; geri yükleme sırasında mevcut SMTP şifreniz korunur.
