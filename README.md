# World Cup Predictor

A client-side Blazor WebAssembly app for building a World Cup prediction. Users rank group-stage teams with drag and drop, generate a configurable Round of 32 bracket, then click teams through each knockout round until a champion is selected.

## Run locally

```powershell
dotnet restore
dotnet run
```

Open the local URL shown by `dotnet run`. During development you can also run:

```powershell
dotnet build
```

## Project structure

```text
Components/
  AppHeader.razor
  ChampionCard.razor
  DraggableTeamList.razor
  GroupCard.razor
  KnockoutBracket.razor
  KnockoutRoundColumn.razor
  MatchupCard.razor
  ProgressStepper.razor
  TeamCard.razor
Models/
  Group.cs
  GroupStanding.cs
  KnockoutMapping.cs
  KnockoutRound.cs
  KnockoutSlot.cs
  Matchup.cs
  PredictionState.cs
  Team.cs
Pages/
  GroupStage.razor
  Home.razor
  KnockoutStage.razor
Services/
  BracketGenerator.cs
  LocalStorageService.cs
  PredictionStateService.cs
  TournamentData.cs
wwwroot/css/app.css
```

## Notes

- The app is client-side only and uses browser local storage through `LocalStorageService`.
- Placeholder teams are seeded in `Services/TournamentData.cs`.
- The Round of 32 slot mapping is configured in `TournamentData.RoundOf32Mapping`.
- Best third-place slot selection uses `TournamentData.BestThirdPlaceGroupPriority` and each slot's eligible groups.
- Bracket generation and winner propagation live in `Services/BracketGenerator.cs`, keeping tournament logic out of UI components.
