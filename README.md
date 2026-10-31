# OpenHwid

Otwarta aplikacja dla Windows do odczytu identyfikatorów sprzętu oraz zarządzania adresem MAC obsługiwanym przez sterownik karty. Bez kont, telemetrii, płatności i limitów użyć. Wersja **0.2.0**, licencja MIT.

Samodzielna implementacja. Projekt nie jest powiązany z hwidchange.com i nie zawiera jego kodu ani zasobów.

## Zakres

| Funkcja | Wersja 0.2 |
| --- | --- |
| Odczyt UUID SMBIOS, BIOS, płyty głównej, CPU, dysków i woluminów | Tak, lokalne CIM/WMI |
| Lista kart sieciowych i aktualne MAC | Tak |
| Eksport raportu JSON | Tak |
| Losowanie lokalnego adresu unicast | Tak, generator kryptograficzny |
| Zmiana MAC | Dla włączonych kart ze wsparciem `NetworkAddress` w sterowniku |
| Kopia i przywracanie ustawień MAC | Tak, zachowywana pierwsza kopia dla każdej karty |
| Nazwane profile MAC | Zapis, wczytanie do edytora i usuwanie |
| Porównanie raportów „przed i po” | Zmiany właściwości, dodane/usunięte urządzenia i braki odczytu |
| Eksport różnic JSON | Tak, wraz z oboma porównywanymi raportami |
| Zmiana UUID SMBIOS, firmware i numeru seryjnego dysku | Nie |

HWID nie jest pojedynczym, uniwersalnym identyfikatorem. Program nie obiecuje, że zmiana MAC zmieni sposób rozpoznawania urządzenia przez dowolną aplikację. Nie zawiera sterownika jądra, modyfikacji firmware ani mechanizmów ingerujących w inne programy.

## Wymagania i uruchomienie

- Windows 10/11 x64; inne architektury nie były zweryfikowane.
- Do budowania: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Do uruchamiania zwykłego buildu: .NET 10 Desktop Runtime. Publikacja `--self-contained true` zawiera runtime.
- Wbudowany Windows PowerShell 5.1, moduł NetAdapter i dostępny lokalny dostawca CIM/WMI.
- Odczyt nie wymaga administratora. Zmiana i przywracanie MAC wymagają uruchomienia jako administrator.

```powershell
dotnet build OpenHwid.slnx -c Release
dotnet run --project src/OpenHwid.Windows -c Release
```

Publikacja samodzielnej aplikacji x64:

```powershell
dotnet publish src/OpenHwid.Windows -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Uruchom `artifacts/win-x64/OpenHwid.exe`. Opublikowane pliki nie są podpisane certyfikatem wydawcy.

## Zmiana MAC

1. Uruchom aplikację. W zakładce sprzętu możesz odczytać dane i wyeksportować JSON.
2. Przejdź do zakładki **Adres MAC i przywracanie**. W razie potrzeby uruchom aplikację jako administrator.
3. Wybierz kartę, wylosuj adres lub wpisz własny lokalny adres unicast. Akceptowane formaty: `02AABBCCDDEE`, `02:AA:BB:CC:DD:EE`, `02-AA-BB-CC-DD-EE`.
4. Kliknij **Zmień MAC** i potwierdź restart karty. Połączenie tej karty zostanie przerwane; unikaj wykonywania tego przez połączenie zdalne zależne od tej karty.
5. Aplikacja zachowa kopię, zapisze nadpisanie, zrestartuje kartę i porówna odczytany MAC z żądanym. Odmowa sterownika lub niezgodność nie jest przedstawiana jako sukces.

Nie każda karta obsługuje nadpisanie MAC. Wi-Fi, sterowniki OEM, karty wirtualne oraz systemowe losowanie adresów mogą ograniczać działanie. Program konserwatywnie udostępnia zmianę tylko wtedy, gdy sterownik deklaruje parametr `NetworkAddress`. Jego obecność nie gwarantuje akceptacji każdej wartości. Może być potrzebny restart systemu.

## Profile MAC

W zakładce **Profile MAC** podaj nazwę (1–64 znaki) i lokalny adres unicast. Możesz skopiować adres z pola w zakładce sieci. **Zapisz profil** zachowuje adres lokalnie; nazwy są unikalne bez rozróżniania wielkości liter.

Wybierz profil i kliknij **Wczytaj do pola MAC**. Program przejdzie do zakładki sieci i uzupełni adres. Wybierz kartę oraz użyj **Zmień MAC**, aby go zastosować. Sam zapis, wczytanie lub usunięcie profilu nie zmienia sieci ani kopii ustawień. Profile nie są przypisane do konkretnej karty.

Pliki są w `%LOCALAPPDATA%\OpenHwid\profiles\<GUID-profilu>.json`. Nazwa wyświetlana nie jest używana jako ścieżka pliku. Zapis jest atomowy; uszkodzone pliki nie są nadpisywane. Profile są niezaszyfrowane, tak jak kopie ustawień.

## Porównanie raportów

1. W zakładce **Porównanie raportów** kliknij **Zapamiętaj bieżący**, aby zachować bazę na czas sesji, lub **Wczytaj bazowy JSON**, aby użyć wcześniejszego eksportu sprzętu.
2. Po zmianie ustawień albo sprzętu kliknij **Odśwież**. Tabela pokaże różnice między bazą a aktualnym odczytem. Baza nie jest automatycznie zastępowana po odświeżeniu.
3. **Eksportuj różnice** zapisuje plik z różnicami **oraz oboma raportami**. Zawiera on identyfikatory sprzętu; przejrzyj go przed udostępnieniem. To osobny format, nie raport bazowy do ponownego importu.

Karty są dopasowywane po GUID, woluminy po `DeviceID`, a CPU i dyski po `DeviceID`, gdy oba raporty go zawierają. Są to identyfikatory urządzeń w Windows, nie gwarancja niezmienności po przeinstalowaniu lub ponownej enumeracji sprzętu. Czas wykonania i kolejność wyników nie tworzą różnic. Karty bez GUID lub ze zduplikowanym GUID są porównywane jako osobna grupa; nie blokują dopasowania pozostałych kart. Dla innych niejednoznacznych albo starszych danych porównywany jest cały uporządkowany zestaw kategorii, bez zgadywania par urządzeń. Nowo dostępne właściwości mogą pojawić się jako dodane pola.

Nieudane zapytanie lub brak kategorii oznacza **Brak odczytu**, a nie usunięcie sprzętu. Limit importowanego raportu to 5 MiB; obsługiwany jest schemat 1. Oryginalne raporty są tylko odczytywane.

## Przywracanie i kopie

Kopie znajdują się w `%LOCALAPPDATA%\OpenHwid\backups\<GUID-karty-bez-myślników>.json`. Są lokalne, niezaszyfrowane i zawierają poprzedni MAC oraz skrót identyfikatora instalacji Windows. Program nie wysyła ich do sieci.

- Pierwsza kopia zostaje zachowana przy kolejnych zmianach oraz po przywróceniu. „Oryginał” oznacza stan przed pierwszą zmianą tą aplikacją, niekoniecznie adres fabryczny.
- Jeżeli przed zmianą nie było `NetworkAddress`, przywracanie usuwa tę wartość. Jeżeli była, przywraca jej wcześniejszą treść.
- Zapis kopii musi się udać przed zmianą. Uszkodzona kopia blokuje zmianę; kopia z innego komputera lub karty nie jest stosowana.
- Błąd restartu albo weryfikacji nie usuwa kopii i nie uruchamia automatycznej drugiej zmiany. Użyj **Przywróć z kopii** i sprawdź stan interfejsu. Przywracanie również restartuje kartę.
- Przy zmianie użytkownika Windows lub uruchomieniu jako inne konto administratora katalog kopii będzie inny. Do przywrócenia użyj tego samego konta.
- Przeinstalowanie karty lub Windows może zmienić identyfikatory i uniemożliwić automatyczne użycie starej kopii.

Jeżeli karta pozostanie wyłączona po błędzie, włącz ją w ustawieniach sieciowych Windows. Gdy aplikacja nie może przywrócić ustawień, sprawdź kopię i ustawienie **Adres sieciowy / Network Address** we właściwościach zaawansowanych karty w Menedżerze urządzeń. Usuń nadpisanie tylko wtedy, gdy kopia wskazuje, że wcześniej go nie było. Nie kasuj kopii przed odzyskaniem połączenia.

Raport JSON zawiera identyfikatory urządzenia — przejrzyj go przed publikacją. Nie dołączaj własnych raportów ani kopii do repozytorium.

## Testy i weryfikacja

Bez zewnętrznego frameworka testowego:

```powershell
dotnet run --project tests/OpenHwid.Tests -c Release
```

Testy wykonują prawdziwe operacje na plikach w katalogach tymczasowych, używając zastępczej karty sieciowej. Obejmują walidację MAC, ochronę i przywracanie kopii, odmowę sterownika, błędy zapisu/restartu, porównywanie raportów, niepełne odczyty oraz zapis/usuwanie i walidację profili. Nie zmieniają sieci komputera. Kod wyjścia jest niezerowy przy błędzie.

Test uruchomienia interfejsu i odczytu lokalnego sprzętu, bez zmiany ustawień (Windows):

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
$app = Start-Process -FilePath .\src\OpenHwid.Windows\bin\Release\net10.0-windows\OpenHwid.exe -ArgumentList '--smoke-test', "$PWD\artifacts\smoke.txt" -PassThru -Wait -WindowStyle Hidden
if ($app.ExitCode -ne 0) { throw 'Smoke test failed' }
Get-Content artifacts/smoke.txt
```

Test zapisuje pod wskazaną ścieżką wynik oraz obok PNG zakładek. Sprawdza odczyt sprzętu, porównanie identycznych raportów i symulowanej zmiany MAC (gdy dostępna jest karta w raporcie) oraz zapis i wczytanie profilu do edytora. Profil testowy trafia do `smoke-profiles` obok wyniku i jest usuwany po sprawdzeniu. Ustawienia sieciowe i osobiste profile nie są zmieniane. Obrazy zawierają prawdziwe identyfikatory sprzętu — nie publikuj ich bez anonimizacji.

**Granica weryfikacji:** testy automatyczne nie potwierdzają działania rzeczywistej zmiany MAC na każdym sterowniku. Przed wydaniem przetestuj zmianę i przywracanie na osobnej maszynie/kartach, w tym awarię restartu. Projekt jest na wczesnym etapie rozwoju.

## Struktura i wkład

- `src/OpenHwid.Core`: walidacja, generator, plikowe kopie, przebieg zmiany/przywracania, profile i porównanie raportów.
- `src/OpenHwid.Windows`: WinForms, odczyt CIM, rejestr i restart kart przez NetAdapter.
- `tests/OpenHwid.Tests`: wykonywalny zestaw testów; zakończenie kodem 1 oznacza błąd.

Zmiany zgłaszaj przez pull request z opisem zachowania i wynikami testów. Nie umieszczaj raportów sprzętu w zgłoszeniach. Nie zmieniaj schematu kopii bez planu migracji. Zobacz [CONTRIBUTING.md](CONTRIBUTING.md).

Dokumentacja używanych interfejsów Windows: [Restart-NetAdapter](https://learn.microsoft.com/en-us/powershell/module/netadapter/restart-netadapter), [Win32_ComputerSystemProduct](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-computersystemproduct).
