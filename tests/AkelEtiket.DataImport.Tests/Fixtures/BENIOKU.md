# Test dosyaları

## eski-excel.xls

Eski Excel (Excel 97–2003, BIFF8) okuyucusunun testleri (`XlsImportTests`, Design testlerinde `DataFileToolTests`)
bu dosyayı okur. Gerçek bir OLE2 bileşik belge + BIFF8 çalışma kitabıdır (5 632 bayt).

**Nasıl üretildi:** `eski-excel.py` betiği ile, [xlwt](https://pypi.org/project/xlwt/) 1.3.0 (BSD lisanslı, saf Python)
kullanılarak:

```
python -m pip install xlwt==1.3.0
python eski-excel.py eski-excel.xls
```

Üretim belirlenimcidir (aynı betik aynı baytları verir). 13.10.2026'da üretilen dosyanın SHA-256 özeti:
`6ea37124d78fe31d35503895ce8112f409c1b0b978550ab2621d91904021a894`

xlwt yalnızca bu dosyayı üretmek için kullanıldı; uygulamaya ya da test projelerine bağımlılık olarak girmez.
Testler için Python gerekmez.

İçerik (testlerin dayandığı durumlar):

| Sayfa | Durum |
|---|---|
| Boş Sayfa | Görünür, boş → varsayılan seçimde atlanır |
| Gizli | Gizli, veri var → varsayılan seçimde atlanır, adıyla seçilebilir |
| Ürünler | 1. satır boş, başlık 2. satırda, veri 3–4 ve 6. satırlarda (5. satır boş): baştaki sıfırlı metin barkod, 13 haneli sayı, `0000000000000` biçimi, `0.00` / `#,##0.00` ondalık, tarih ve tarih-saat, mantıksal değer, yüzde, 15 haneden uzun sayı, bilimsel gösterimde metin, `#N/A` hata hücresi, boşluklu metin |
| Fiyatlar | Türkçe büyük harfle (`FİYATLAR`) seçilebilmesi için ikinci veri sayfası |
