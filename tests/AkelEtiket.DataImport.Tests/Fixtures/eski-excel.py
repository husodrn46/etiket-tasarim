# -*- coding: utf-8 -*-
"""eski-excel.xls test dosyasını üretir (Excel 97-2003 / BIFF8).

Kullanım:
    python -m pip install xlwt==1.3.0
    python eski-excel.py eski-excel.xls

İsteğe bağlı: xlwt kurulu değilse paketin açıldığı klasör ikinci argüman olarak verilebilir
    python -I eski-excel.py eski-excel.xls C:\\yol\\xlwt-klasoru

xlwt (BSD lisanslı, saf Python) yalnızca bu dosyayı üretmek için kullanılır; uygulamaya ve testlere
bağımlılık olarak girmez. Testler üretilmiş .xls dosyasını okur (XlsImportTests).
"""
import datetime
import sys

if len(sys.argv) > 2:
    sys.path.insert(0, sys.argv[2])

import xlwt  # noqa: E402

out = sys.argv[1]
wb = xlwt.Workbook(encoding="utf-8")

# 0) Görünür ama boş sayfa: varsayılan seçimde atlanmalı.
wb.add_sheet("Boş Sayfa")

# 1) Gizli sayfa: veri olsa da varsayılan seçimde atlanmalı (adıyla seçilebilir).
hidden = wb.add_sheet("Gizli")
hidden.visibility = 1
hidden.write(0, 0, "Gizli veri")
hidden.write(1, 0, "x")

# 2) Asıl veri: 1. satır boş, başlık 2. satırda; 5. satır boş (atlanmalı).
ws = wb.add_sheet("Ürünler")
headers = ["Kod", "Ürün Adı", "Barkod", "Barkod Sayı", "Barkod Biçimli", "Fiyat", "Adet",
           "Tarih", "Tarih Saat", "Aktif", "İndirim", "Uzun", "Bilimsel", "Hata"]
for c, h in enumerate(headers):
    ws.write(1, c, h)

pad13 = xlwt.easyxf(num_format_str="0000000000000")
money = xlwt.easyxf(num_format_str="0.00")
thousands = xlwt.easyxf(num_format_str="#,##0.00")
date = xlwt.easyxf(num_format_str="dd.mm.yyyy")
date_time = xlwt.easyxf(num_format_str="dd.mm.yyyy hh:mm")
percent = xlwt.easyxf(num_format_str="0%")
percent1 = xlwt.easyxf(num_format_str="0.0%")

# Excel satırı 3
ws.write(2, 0, "AKL001")
ws.write(2, 1, "Melamin Kase")
ws.write(2, 2, "0012345678905")            # metin: baştaki sıfırlar korunmalı
ws.write(2, 3, 8691234567890)              # sayı (Genel): 13 hane, bilimsel gösterim olmadan
ws.write(2, 4, 12345678905, pad13)         # "0000000000000" biçimi: sıfırla doldurulmalı
ws.write(2, 5, 12.5, money)                # "12,50"
ws.write(2, 6, 3)
ws.write(2, 7, datetime.date(2026, 10, 13), date)
ws.write(2, 8, datetime.datetime(2026, 10, 13, 14, 30), date_time)
ws.write(2, 9, True)
ws.write(2, 10, 0.2, percent)
ws.write(2, 11, 12345678901234567)         # 15 haneden uzun: hata
ws.write(2, 12, "8,69123E+12")             # bilimsel gösterimde metin: uyarı
ws.row(2).set_cell_error(13, 0x2A)        # Excel hata değeri #N/A (BIFF kodu 0x2A): uyarı, boş kabul

# Excel satırı 4
ws.write(3, 0, "AKL002")
ws.write(3, 1, "Çay Bardağı IŞIKLI")
ws.write(3, 2, "0012345678912")
ws.write(3, 3, 8690000000013)
ws.write(3, 4, 1234, pad13)
ws.write(3, 5, 1234.5, thousands)          # "1.234,50"
ws.write(3, 6, 0)
ws.write(3, 7, datetime.date(2026, 1, 2), date)
ws.write(3, 8, datetime.datetime(2026, 1, 2, 0, 0), date_time)
ws.write(3, 9, False)
ws.write(3, 10, 0.155, percent1)
ws.write(3, 11, 42)

# Excel satırı 5 boş; satır 6: boşluklu metin (kırpılmalı), Genel ondalık
ws.write(5, 0, "  AKL003  ")
ws.write(5, 1, "Servis Tabağı")
ws.write(5, 5, 0.1)
ws.write(5, 6, 2)

# 3) İkinci veri sayfası (Türkçe büyük harfle "FİYATLAR" olarak da seçilebilmeli).
prices = wb.add_sheet("Fiyatlar")
prices.write(0, 0, "Kod")
prices.write(0, 1, "Fiyat")
prices.write(1, 0, "AKL001")
prices.write(1, 1, 19.9)
prices.write(2, 0, "AKL002")
prices.write(2, 1, 7)

wb.save(out)
print("yazıldı:", out)
