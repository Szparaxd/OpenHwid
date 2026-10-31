# Changelog

## 0.1.0 — 2026-10-31

- Polski interfejs Windows do odczytu identyfikatorów sprzętu przez CIM.
- Eksport lokalnego raportu JSON.
- Generator lokalnych adresów MAC unicast i obsługa zmiany przez sterownik.
- Zachowanie pierwszej kopii ustawień dla każdej karty oraz przywracanie.
- Weryfikacja rzeczywistego MAC po restarcie karty.
- Testy logiki i kopii, odczytowy test uruchomienia interfejsu oraz Windows CI.

Ograniczenie: rzeczywista zmiana/przywracanie MAC wymaga testów na konkretnych kartach. Brak zmian SMBIOS, firmware i numerów seryjnych dysków.
