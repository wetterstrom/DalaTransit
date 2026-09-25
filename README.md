# 🚌 DalaTransit

**DalaTransit** är en modern, mobilfokuserad Progressive Web App (PWA) byggd i Blazor WebAssembly för resenärer och pendlare i Dalarna. 

Appen kombinerar en snabb reseplanerare med en datadriven punktlighetsrapport. Istället för att enbart lita på tidtabeller analyserar DalaTransit faktiska realtidsavvikelser för att ge en ärlig riskbedömning vid byten vid viktiga knutpunkter (såsom Falun Knutpunkten och Borlänge Centralstation).

---

## ✨ Funktioner

* **Reseplanerare med anslutningsradar:** Sök avgångar och få en visuell dragspelsvy över etapper, bytestider och riskbedömning för snäva anslutningar.
* **Punktlighetsstatistik i realtid:** Analys av faktiska avgångar från Dalatrafiks stombussar och tåglinjer (t.ex. linje 151 Falun–Borlänge).
* **Identifiering av högriskbyten:** Belyser bytesrelationer där tidtabellens marginal ofta spricker i praktiken på grund av återkommande förseningar.
* **PWA & Mobiloptimerad:** Designad från grunden för mobilskärmar med touch-vänliga reglage, bottennavigering och stöd för offline-visning via Service Workers.
* **Serverlös automationspipeline:** Kräver ingen dedikerad backendserver. Ett schemalagt GitHub Actions-arbetsflöde hämtar kontinuerligt data från Trafiklab och uppdaterar appens statiska datakälla.

---

## 🛠️ Teknisk stack

* **Frontend:** [Blazor WebAssembly](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) (.NET 10 / .NET 8)
* **Datainsamling & CLI:** C# .NET konsolapplikation (`DalaTransit.Cli`)
* **Databas & ORM:** SQLite (`transit.db`) & Entity Framework Core
* **Externa API:er:** [Trafiklab](https://www.trafiklab.se/) (ResRobot v2.1 & Realtime API)
* **CI/CD & Hosting:** GitHub Actions (automatiserad datagenerering var 6:e timme) och GitHub Pages

---

## 🏗️ Arkitektur & Dataflöde

1. **Schemalagd insamling:** Ett GitHub Actions-jobb körs var 6:e timme via cron.
2. **CLI-analys:** `DalaTransit.Cli` anropar ResRobot API, läser av hundratals realtidsavgångar, kalkylerar snittförseningar och punktlighetsprocent.
3. **Dataexport:** Resultatet aggregeras och sparas som en optimerad `punctuality.json` inuti frontendens `wwwroot/data/`.
4. **Distribution:** Appen byggs och driftsätts direkt till GitHub Pages där användaren konsumerar färska data blixtsnabbt utan API-kvotbegränsningar i klienten.

---

## 🚀 Kom igång lokalt

### Förutsättningar
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download) eller senare
* API-nyckel för **ResRobot v2.1** från [Trafiklab.se](https://www.trafiklab.se/)

### 1. Klona repot
```bash
git clone [https://github.com/DITT-ANVÄNDARNAMN/DalaTransit.git](https://github.com/DITT-ANVÄNDARNAMN/DalaTransit.git)
cd DalaTransit
