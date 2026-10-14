# Budget Tracker

A console-based Budget Tracker written in C#. Users can add and remove income or expense transactions, which are stored as one JSON file per day, and generate reports for a date range with LINQ — all from a simple terminal menu.

## Requirements

- .NET 10.0 SDK (or later)
- No external NuGet packages required (uses `System.Text.Json` built into the framework)

## Build & Run

```bash
# From the project directory
dotnet build

# Run the application
dotnet run
```

Or build and run the DLL directly:

```bash
dotnet build
dotnet bin/Debug/net10.0/BudgetTracker.dll
```

## Project Structure

```
BudgetTracker/
├── Program.cs                          # Main menu loop, user interaction, directory bootstrap
├── BudgetTracker.csproj
├── Models/
│   ├── Transaction.cs                  # Record with Id, Timestamp, Type, Description, Amount
│   └── TransactionType.cs              # Enum: Income, Expense
├── Events/
│   └── TransactionAddedEventArgs.cs    # EventArgs subclass exposing the added Transaction
├── Services/
│   ├── StorageService.cs               # All JSON file I/O (save, load, remove)
│   ├── TransactionService.cs           # Business logic: add, remove, query; raises events
│   └── LoggerService.cs                # Subscribes to TransactionAdded; writes to logs/
├── data/                               # Generated on first run — daily JSON files
└── logs/                               # Generated on first run — transactions.log
    └── transactions.log
```

### Namespaces

- `BudgetTracker.Models` — domain models (Transaction, TransactionType)
- `BudgetTracker.Events` — event argument types (TransactionAddedEventArgs)
- `BudgetTracker.Services` — business and storage services

## Menu Options

```
┌─────────────────────────────┐
│          MAIN MENU          │
├─────────────────────────────┤
│ 1. Add Transaction          │
│ 2. Remove Transaction       │
│ 3. Generate Report          │
│ 4. Exit                     │
└─────────────────────────────┘
```

### 1. Add Transaction

Prompts for:
- **Type**: `Income` or `Expense` (parsed with `Enum.TryParse`, case-insensitive; re-prompts on invalid input)
- **Description**: free-text description
- **Amount**: positive decimal (parsed with `decimal.TryParse`; re-prompts on invalid input)

Sets `Id = Guid.NewGuid()` and `Timestamp = DateTimeOffset.Now`, then stores the transaction in `data/yyyy-MM-dd.json`.

### 2. Remove Transaction

Prompts for a transaction GUID. Searches every file in `data/` for a matching transaction. If found, removes it from the day file and rewrites that file. If not found, reports that no transaction has that Id.

### 3. Generate Report

Prompts for:
- **Start date** in `yyyy-MM-dd` format
- **End date** in `yyyy-MM-dd` format
- **Optional type filter**: `Income`, `Expense`, or `None` (show all)

Loads only the day files whose filename falls within the date range, flattens them with `SelectMany`, and prints:
- Each transaction with Id, Date, Type, signed Amount, and Description
- **Total Income** (sum of all Income transactions)
- **Total Expenses** (sum of all Expense transactions)
- **Balance** (Total Income − Total Expenses)

### 4. Exit

Exits the application.

## Data Storage

Transactions are stored as JSON arrays in daily files:

```
data/
├── 2026-03-14.json
├── 2026-03-15.json
└── 2026-03-16.json
```

Each file contains a JSON array of Transaction objects. Enums are serialized as text (e.g. `"Income"`, `"Expense"`).

Example `data/2026-03-14.json`:

```json
[
  {
    "Id": "a1b2c3d4-...",
    "Timestamp": "2026-03-14T10:30:00+00:00",
    "Type": "Income",
    "Description": "Freelance work",
    "Amount": 150.00
  }
]
```

## Error Handling

- **Bad input**: Invalid enum values, non-numeric amounts, malformed dates, and invalid GUIDs are caught and the user is re-prompted.
- **I/O errors**: File access failures are caught and reported with a clear message.
- **Invalid JSON**: Corrupt data files produce an `InvalidDataException` with the offending filename.
- **Program never crashes**: All menu actions are wrapped in try/catch; any unanticipated error is reported as a message and the program continues.

## Events

`TransactionService` raises a `TransactionAdded` event every time a transaction is successfully added. `LoggerService` subscribes to this event and appends one line per transaction to `logs/transactions.log`:

```
[2026-03-14 10:30:00] + 150,00 - Freelance work (Id: a1b2c3d4-...)
```

## License

This is a learning project.

## Security hardening

- JSON imports are strictly validated on load; invalid enum values, IDs, amounts, descriptions, dates, and oversized files are rejected.
- Integer values are not accepted for `TransactionType` during JSON deserialization.
- Daily transaction counts and description/amount/file sizes are bounded.
- Data writes use a per-file lock and an atomic temporary-file replacement to reduce lost updates and corruption risk.
- Log and terminal output strips control characters to prevent log/terminal injection.
- EOF is handled explicitly instead of looping forever on redirected input.
- Runtime data, logs, build artifacts, and Git metadata are excluded from source control via `.gitignore`.

Runtime `data/` and `logs/` directories are created automatically when the application starts.

## Additional feature: Spending budget

The project includes an optional spending-budget feature. On first start, the user chooses a budget which is stored in `data/budget.json`. Income and top-ups increase the available funds, expenses reduce them: remaining budget = budget + income − expenses, which is always equal to the account balance. A configurable warning (20% by default) is raised through an event when the budget runs low. The Budget Settings menu also supports custom top-ups and a bonus top-up of **7,777,777**.

See `ERKLAERUNG_BUDGETFEATURE.md` for a German walkthrough intended for the project presentation.

## Web UI (frontend)

The project now ships with a browser frontend that is wired directly to the existing C# services (no mock data). `WebApp.cs` only exposes `TransactionService` / `BudgetService` as a small local HTTP API; all validation, storage, logging and budget events still run through the original classes and the same `data/` and `logs/` folders.

```bash
dotnet run                # web UI -> http://localhost:5080
dotnet run -- --console   # original terminal menu
```

- Every **expense** shows a sad smiley overlay.
- Every **top-up** (custom amount or the 7,777,777 bonus) lets bills rain down.
- Budget warnings from the `BudgetWarning` event are shown as a banner/toast.
- The server only listens on `localhost`. Static files live in `wwwroot/`.

| Endpoint | Purpose |
|---|---|
| `GET /api/state` | budget, spent, remaining, totals |
| `POST /api/setup` | first-start budget |
| `GET /api/transactions?from=&to=&type=` | report with filters |
| `POST /api/transactions` | add transaction |
| `DELETE /api/transactions/{id}` | remove transaction |
| `POST /api/budget/topup`, `POST /api/budget/bonus` | top up budget |
| `PUT /api/budget/threshold` | change warning threshold |
