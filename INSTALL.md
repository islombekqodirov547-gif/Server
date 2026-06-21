# JESKO Server — o'rnatish va sozlash yo'riqnomasi

Bu server endi **alohida Windows dasturi** bo'lib ishlaydi:
- Soat yonida (system tray) **"J"** ikonkasi chiqadi — server ishlayotganini bildiradi.
- Ikonkani ikki marta bossangiz **Sozlamalar** oynasi ochiladi.
- Ma'lumotlar bazasi **SQLite** (bitta fayl) — boshqa hech narsa o'rnatish kerak emas.
- Kompyuter yonib, foydalanuvchi kirganda server **avtomatik** ishga tushadi.
- Tarmoq: **HTTP**, standart port **5050**, barcha tarmoq interfeyslarida tinglaydi.

---

## 1. Dasturchi kompyuterida: build va setup yasash

Kerak bo'ladi (faqat setup yasash uchun, bir marta):
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- **Inno Setup** — https://jrsoftware.org/isdl.php

Qadamlar:
1. `installer\publish.bat` ni ishga tushiring.
   - Bu `installer\publish\` papkasiga **self-contained** (ichida .NET runtime bor) fayllarni yig'adi.
   - Natijada `JESKO.Server.exe` hosil bo'ladi — bu boshqa PC'da .NET'siz ishlaydi.
2. `installer\StoreServer.iss` faylini **Inno Setup** da oching va **Compile (F9)** bosing.
3. `installer\Output\JESKO-Server-Setup.exe` hosil bo'ladi — bu yagona o'rnatuvchi fayl.

> Buyruq qatoridan publish (xohlasangiz):
> ```
> dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o installer\publish
> ```

---

## 2. Do'kon (server) kompyuterida: o'rnatish

1. `JESKO-Server-Setup.exe` ni server bo'ladigan kompyuterga ko'chiring va ishga tushiring (**Administrator** sifatida).
2. O'rnatish quyidagilarni avtomatik bajaradi:
   - Dasturni `C:\Program Files\JESKO Server\` ga o'rnatadi.
   - **Avto-ishga tushish** yorlig'ini qo'shadi (Startup).
   - **Windows Firewall** da serverga kiruvchi ulanishlarга ruxsat beradi.
3. O'rnatish oxirida server ishga tushadi — soat yonida **"J"** ikonkasi paydo bo'ladi.

### Birinchi kirish (login)
Yangi o'rnatishda standart admin: **login: `admin`**, **parol: `admin123`**.

---

## 3. Server manzilini bilish (ilovalarga yozish uchun)

Soat yonidagi **"J"** ikonkasini ikki marta bosing → **Sozlamalar** oynasi ochiladi:
- Yuqorida shu kompyuterning **IP manzillari** ko'rsatiladi (masalan `192.168.1.2`).
- Pastda ilovalarga yoziladigan **to'liq manzil** chiqadi, masalan: `http://192.168.1.2:5050/`
- "Manzilni nusxalash" tugmasi bilan nusxalab olishingiz mumkin.

Aynan shu manzilni:
- **Mobil ilova**da: Login ekrani → yuqori o'ngdagi ⚙ → "Server manzili" ga yozasiz
  (faqat IP yozsangiz ham bo'ladi, masalan `192.168.1.2` — port avtomatik 5050 bo'ladi).
- **Desktop ilova**da (admin/kassir/buxgalter): Login oynasi → yuqori o'ngdagi ⚙ tugma → server manzilini yozasiz.

---

## 4. Tarmoq (WiFi/LAN) bo'yicha muhim eslatmalar

- Server kompyuteri va barcha qurilmalar **bitta WiFi/router**ga ulangan bo'lishi shart.
- Server kompyuteriga **statik IP** berish tavsiya etiladi (masalan `192.168.1.2`), shunda manzil o'zgarmaydi.
  - Router sozlamasidan yoki Windows: Tarmoq sozlamalari → IPv4 → qo'lda IP.
- Agar ulanmasa: server PC da Windows Firewall qoidasi borligini tekshiring
  (installer buni o'zi qo'shadi; kerak bo'lsa antivirus firewallini ham tekshiring).

## 5. Kompyuter o'chib-yonsa

- Avto-ishga tushish yorlig'i tufayli, foydalanuvchi Windows'ga kirgach server o'zi ishga tushadi.
- "Power on → server ishlaydi" bo'lishi uchun Windows'da **avtomatik login**ni yoqing
  (do'kon kompyuteri uchun qulay). Buni Windows hisob sozlamalaridan yoqasiz.

## 6. Portni o'zgartirish (kamdan-kam kerak)

Sozlamalar oynasida portni o'zgartirib "Saqlash" bosing, so'ng ikonka → **Chiqish** va dasturni
qayta oching. Port o'zgarsa ilovalardagi manzilni ham yangilang.

---

## Texnik tafsilotlar
- Sozlama fayli: `C:\ProgramData\StoreSystem\server-config.json`
- Ma'lumotlar bazasi: `C:\ProgramData\StoreSystem\store.db` (SQLite)
- Swagger (test uchun): server PC da `http://localhost:5050/swagger`
