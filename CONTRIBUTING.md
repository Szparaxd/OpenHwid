# Wkład w OpenHwid

1. Używaj Windows i .NET 10 SDK. Zbuduj `dotnet build OpenHwid.slnx -c Release`.
2. Uruchom pełny zestaw `dotnet run --project tests/OpenHwid.Tests -c Release`.
3. Dla zmian interfejsu wykonaj test uruchomienia z README i sprawdź układ przy skalowaniu ekranu.
4. Dla zmian zapisu sieci dodaj testy ochrony kopii i niepowodzeń. Testy automatyczne nie mogą zmieniać rzeczywistych kart.
5. W PR opisz problem, wynik oraz zakres rzeczywistej weryfikacji. Numery seryjne, MAC, UUID i kopie muszą pozostać prywatne.

Projekt przyjmuje wkład na licencji MIT. Zgłaszając kod, potwierdzasz prawo do udostępnienia go na tej licencji.
