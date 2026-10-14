# Erklärung der Zusatzfunktion: Budget

## Idee

Zusätzlich zu den normalen Einnahmen und Ausgaben kann der Benutzer ein persönliches **Ausgabenbudget** festlegen. Budget, Einnahmen und Kontostand sind dabei **synchron**: Das Budget besteht aus dem Startbetrag plus allen Aufladungen. **Einnahmen erhöhen das verfügbare Geld, Ausgaben verringern es.** Das verbleibende Budget ist deshalb immer gleich dem Kontostand.

Beim ersten Start wird nach einem Startbudget gefragt. Dieses wird in `data/budget.json` gespeichert und ist deshalb beim nächsten Programmstart wieder vorhanden.

## Neue Ordner und Dateien

- `Models/Budget.cs` – Datenmodell für Gesamtbudget und Warnschwelle.
- `Services/BudgetService.cs` – enthält die eigentliche Budget-Logik und berechnet ausgegebenes/verbleibendes Budget.
- `Services/BudgetStorageService.cs` – liest und schreibt `data/budget.json`.
- `Events/BudgetWarningEventArgs.cs` – Daten für das Budget-Warn-Event.
- `Helpers/ConsoleHelper.cs` – wiederverwendbare Methoden für Konsoleneingaben und Zahlenvalidierung.
- `Program.cs` – zeigt die Budgetübersicht an und enthält nur die Menü-/Benutzerinteraktion.

Damit sind Datenmodell, Logik, Speicherung, Events und Benutzeroberfläche voneinander getrennt.

## Wie die Berechnung funktioniert

`BudgetService` lädt alle gespeicherten Transaktionen über den vorhandenen `TransactionService`. Mit LINQ werden nur Transaktionen vom Typ `Expense` ausgewählt und anschließend addiert (für `Income` analog):

```csharp
public decimal SpentAmount => _transactions.GetAllTransactions()
    .Where(t => t.Type == TransactionType.Expense)
    .Sum(t => t.Amount);
```

Genauso wird die Summe aller Einnahmen (`IncomeAmount`) berechnet. Danach gilt:

```text
Verfügbares Geld     = Gesamtbudget (Start + Aufladungen) + Summe aller Einnahmen
Verbleibendes Budget = Verfügbares Geld - Summe aller Ausgaben   (= Kontostand)
```

Der Prozentwert wird aus verbleibendem Budget und verfügbarem Geld berechnet. Eine neue Einnahme oder eine Aufladung erhöht also sofort das Restbudget, eine Ausgabe verringert es.

## Warnfunktion mit Event

Der vorhandene `TransactionService` besitzt bereits das Event `TransactionAdded`. Der neue `BudgetService` hört auf dieses Event. Wird eine neue Ausgabe hinzugefügt, prüft er, wie viel Budget noch übrig ist.

Standardmäßig liegt die Warnschwelle bei **20 %**. Ist der Rest kleiner oder gleich der Warnschwelle, wird das neue Event `BudgetWarning` ausgelöst. Bei 0 € oder weniger wird angezeigt, dass das Budget aufgebraucht ist.

Das ist praktisch, weil die Budgetlogik nicht direkt in die Methode zum Hinzufügen einer Transaktion eingebaut werden musste. Die Klassen bleiben dadurch getrennt.

## Budget Settings

Im Hauptmenü gibt es den neuen Punkt `Budget Settings`. Dort kann man:

1. einen frei gewählten Betrag zum Budget hinzufügen,
2. den Bonusbetrag **7.777.777** hinzufügen,
3. die Warnschwelle ändern,
4. zum Hauptmenü zurückkehren.

Der Bonusbetrag steht als Konstante im `BudgetService`:

```csharp
public const decimal BonusTopUpAmount = 7_777_777m;
```

Die Unterstriche dienen in C# nur der besseren Lesbarkeit. Der Zahlenwert ist genau `7777777`.

## Warum eine eigene JSON-Datei?

Transaktionen werden weiterhin wie von der Aufgabe verlangt tageweise gespeichert. Die Budgeteinstellung gehört aber nicht zu einer einzelnen Transaktion und wird deshalb separat in `data/budget.json` gespeichert. So wird das bestehende Speichersystem nicht unnötig verändert.

Beispiel:

```json
{
  "TotalAmount": 1000,
  "WarningThresholdPercent": 20
}
```

Wenn Geld aufgeladen wird, wird `TotalAmount` erhöht und die Datei erneut gespeichert.

## Was ich bei einer Präsentation erklären würde

> Ich habe das bestehende Projekt um ein Ausgabenbudget erweitert. Beim ersten Start wird das Budget gespeichert. Der BudgetService berechnet mit LINQ die Summe aller Einnahmen und Ausgaben. Verbleibendes Budget = Gesamtbudget + Einnahmen − Ausgaben, und das ist gleichzeitig der Kontostand. Sobald das Restbudget die Warnschwelle erreicht, wird über ein Event eine Warnung ausgelöst. Für die saubere Struktur habe ich Modell, Speicherung, Geschäftslogik, Event-Daten und Konsolen-Hilfsmethoden in getrennte Dateien aufgeteilt. Zusätzlich kann das Budget normal oder über einen Bonus von 7.777.777 aufgeladen werden.

## Testfälle

Zum Vorführen eignen sich diese Fälle besonders gut:

- Startbudget `1000`, Ausgabe `100` → 900 bzw. 90 % übrig, keine Warnung.
- Startbudget `1000`, Einnahme `500` → Restbudget und Kontostand `1500`, danach Ausgabe `300` → `1200`.
- Startbudget `1000`, gesamte Ausgaben `850` → 150 bzw. 15 % übrig, Warnung.
- Startbudget `1000`, gesamte Ausgaben `1000` → Budget aufgebraucht.
- In `Budget Settings` Bonus wählen → Gesamtbudget steigt exakt um `7.777.777`.
- Programm neu starten → Budget und Warnschwelle werden aus `budget.json` geladen.
