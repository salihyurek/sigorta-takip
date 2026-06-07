# Sigorta Takip

Şehirlerarası otobüslerin zorunlu trafik sigortası, kasko ve koltuk sigortası bitiş tarihlerini takip eden yerel web uygulaması.

## Özellikler

- Araç ekleme, düzenleme ve silme
- Zorunlu trafik, kasko ve koltuk sigortası başlangıç/bitiş tarihi takibi
- Bugün biten, günü geçmiş ve 15 gün içinde bitecek poliçeleri dashboard üzerinde vurgulama
- SMTP ayarı yapıldığında günü gelen poliçeler için otomatik e-posta uyarısı
- Manuel e-posta kontrol butonu
- Yönetici girişi, şifre değiştirme ve şifre sıfırlama
- JSON yedek alma ve yedekten geri yükleme

## Çalıştırma

```bash
npm run dev
```

Uygulama:

- Frontend: `http://127.0.0.1:5173/`
- Backend API: `http://127.0.0.1:5001/`

Production build almak için:

```bash
npm run build
npm run start
```

## İlk Giriş

Varsayılan yönetici hesabı:

- E-posta: `salihyurek004@gmail.com`
- Şifre: `Ej+D8q6zhg3kRX*`

Giriş yaptıktan sonra `Ayarlar` bölümünden şifreyi değiştirmeniz önerilir.

## E-posta Bildirimleri

`Ayarlar > E-posta Bildirim Ayarları` bölümünden SMTP bilgilerini girip e-posta bildirimlerini etkinleştirin. Sistem açılışta, her gün 08:00'de ve gün içinde periyodik olarak bugünün tarihinde biten poliçeleri kontrol eder. Aynı poliçe için aynı gün tekrar e-posta göndermez.

Gmail kullanıyorsanız normal hesap şifresi yerine uygulama şifresi gerekir.

## Veri

Tüm veriler `data/db.json` dosyasında tutulur. Arayüzdeki `Yedek İndir` butonu ile JSON yedeği alabilirsiniz.
