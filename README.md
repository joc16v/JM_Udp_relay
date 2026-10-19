# JM stYpe UDP Relay 1.1

**Windows program:** [Preuzmi ZIP iz najnovijeg Releasea](https://github.com/joc16v/JM_Udp_relay/releases/latest).
Odaberi `StypeUdpRelay_v1.1_Windows_x64.zip` pod **Assets**. GitHubov **Code > Download ZIP** sadrzi samo izvorni kod.


Mali Windows x64 UDP razdjelnik: jedan ulaz, proizvoljan broj IPv4 odredišta.
Sadržaj i granice svakog UDP datagrama prosljeđuju se bez izmjena i bez interpretacije stYpe/FreeD protokola.

## Pokretanje

1. Raspakiraj cijeli ZIP. U mapi `app` pokreni `StypeUdpRelay.exe`.
2. Listen IP ostavi na `0.0.0.0` (sva lokalna IPv4 sučelja) ili upiši IP mrežne kartice koja prima tracking.
3. Listen port: `6299`.
4. Odredišta su inicijalno `10.97.17.39:6301` i `10.97.17.48:6301`.
5. U stYpe kitu postavi izlaz na `10.97.17.39:6299`. Viz i dalje sluša svoj postojeći port `6301`.
6. Na računalu u grafici postavi tracking prijam na odabrani port, početno `6301`.
7. Pritisni **Start**. Kad paketi stižu, prikazuju se brojači i brzina prijama.

| Uređaj / program | Postavka |
|---|---|
| stYpe kit — izlaz | `10.97.17.39:6299` |
| Relay na Viz AR računalu — ulaz | `0.0.0.0:6299` |
| Viz tracking — ulaz | `10.97.17.39:6301` |
| Računalo u grafici — ulaz | `10.97.17.48:6301` (promjenjivo) |

Ista IP adresa za ulaz i izlaz dopuštena je ako su portovi različiti. Za lokalni izlaz možeš koristiti i `127.0.0.1:6301`, ako Viz prihvaća promet preko loopbacka; zadana postavka koristi navedenu mrežnu adresu.

## Kontrole i postavke

- Checkbox **Send** uz svako odrediste ukljucuje/iskljucuje slanje odmah tijekom rada. Za drugi komp iskljuci samo njegov red; Viz ostaje ukljucen.
- Promjena checkboxa tijekom rada automatski se sprema. Iskljucena odredista ostaju u popisu.
- Nakon promjene, sljedeci primljeni paketi koriste novi popis; vec zapoceto slanje jednog paketa moze dovrsiti prethodni popis.
- Ako iskljucis sva odredista, prijam ostaje aktivan, ali se paketi ne prosljedjuju.

- Novi red u tablici dodaje odredište. Odaberi cijeli red i pritisni Delete za uklanjanje.
- **Start** sprema postavke i pokreće prijam. Za izmjene adresa/portova i dodavanje/brisanje redova prvo pritisni **Stop**. Checkboxovi su dostupni i tijekom rada.
- **Save settings** sprema postavke bez pokretanja.
- **Start forwarding when app opens** automatski pokreće forwarding pri sljedećem pokretanju programa. To samo po sebi ne dodaje program u Windows Startup.
- Postavke: `%LOCALAPPDATA%\JM Tools\StypeUdpRelay\settings.json`.
- Zatvaranjem prozora prestaje prosljeđivanje; prozor može ostati minimiziran.
- Runtime je uključen. Nije potrebna zasebna .NET instalacija. Zadrži sve datoteke iz mape `app` zajedno.

## Mreža i ponašanje

Windows Firewall treba dopustiti dolazni UDP na 6299 na relay računalu te prijam na odabranim portovima odredišta. Prilagodi postojeća pravila svojoj studijskoj mreži; program ne mijenja firewall.

Primatelj vidi relay računalo kao pošiljatelja i UDP izvorni port 6299. Originalna IP/port adresa stYpe kita nisu sačuvane u mrežnom zaglavlju. Ako prijamnik filtrira izvorni IP/port, prilagodi ga relayu. Tracking payload je nepromijenjen.

Program ne dodaje namjerni delay ni frame buffer. Stvarno dodatno kašnjenje treba izmjeriti na studijskoj mreži. UDP nema potvrdu dostave ni retransmisiju: brojač Sent copies znači da je lokalni sustav prihvatio slanje, a ne da je aplikacija na odredištu primila paket. Nedostupno odredište ne bi smjelo zaustaviti ostala; greške slanja broje se zasebno. Nema povratnog prosljeđivanja komandi.

Duplikati odredišta šalju se samo jednom. Program blokira izravno prosljeđivanje natrag na vlastiti ulazni port. Nemoj povezivati više relaya u kružnu petlju. Podržani su IPv4 unicast UDP paketi; TCP nije podržan.

## Izvorni kod i provjera

U repozitoriju su C# WinForms projekt za .NET 8 i konzolni testovi bez dodatnih biblioteka.

```powershell
dotnet publish .\StypeUdpRelay\StypeUdpRelay.csproj -c Release -r win-x64 --self-contained true -o .\publish
dotnet run --project .\StypeUdpRelay.Tests -c Release
```

Build izvršne verzije je prošao bez upozorenja i grešaka. Prošle su provjere checkbox stanja, neovisnog ukljucivanja odredista, svih iskljucenih odredista, duplikata te kompatibilnosti starih postavki. Prošle su provjere neispravnih adresa/portova, praznog popisa odredišta i izravne lokalne petlje.

GitHub Actions Windows runner uspješno je izvršio cijeli skup testova: tri odredišta, identični payloadovi 29–60000 bajtova, izvorni port, brojači, duplikati, isključivanje i ponovno uključivanje drugog odredišta bez prekida prvog, zauzet port, ponovno pokretanje te nedostupno odredište. [Zapisnik provjere](https://github.com/joc16v/JM_Udp_relay/actions/runs/39337811388).

Lokalni razvojni sandbox blokirao je čak i osnovni UDP loopback test; zato je mrežna provjera provedena na GitHub runneru. Izlazni kod testova 2 označava blokiran osnovni UDP test, 1 grešku, a 0 uspjeh. GUI nije ručno provjeren. Rad sa stvarnim stYpe/Viz sustavom i latencija još nisu potvrđeni.
