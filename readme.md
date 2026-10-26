# C# and .NET Interview Prep

## 📌 Objective
This repository documents my preparation for senior C# / .NET roles. It combines study notes with small practice projects covering OOP, SOLID principles, design patterns, async programming, ASP.NET Core, EF Core, security and system design.

The goal is not only to understand concepts, but to build small projects, find design problems and apply better techniques. Everything here is written from scratch, from public documentation and my own understanding.

## 🚀 Learning Roadmap

```
C# Fundamentals
       ↓
Object-Oriented Programming (OOP)
       ↓
SOLID Principles
       ↓
Async and Multithreading
       ↓
ASP.NET Core Internals
       ↓
EF Core and SQL
       ↓
Security (JWT, OAuth2, OWASP)
       ↓
Design Patterns and Low-Level Design
       ↓
System Design
```

## 📁 Repository Structure

```
dotnet-interview-prep
|
├── README.md
├── <SolutionName>.sln
|
├── Notes
│   ├── Classes & Objects, Constructors, Access Modifiers
│   ├── Encapsulation, Abstraction
│   ├── Aggregation, Composition
│   ├── Exception-Handling
│   ├── Generics
│   └── AsyncAwaitMultiThreading
|
└── Projects
    ├── StudentManagementSystem
    ├── BankAccountManagementSystem
    ├── VehicleManagementSystem
    └── AsyncAwait_MultiThreading
```

## 📊 Progress

| Topic | Notes | Practice | Status |
|---|---|---|---|
| OOP fundamentals (classes, constructors, access modifiers) | ✅ | ✅ | Done |
| Encapsulation, abstraction, composition, aggregation | ✅ | ✅ | Done |
| Exception handling, generics | ✅ | | In progress |
| Async and multithreading | ✅ | ✅ 10 exercises | Done |
| SOLID principles | | | Planned |
| ASP.NET Core internals | | | Planned |
| EF Core and SQL | | | Planned |
| Security (JWT, OAuth2, OWASP) | | | Planned |
| Design patterns and LLD | | | Planned |
| System design practice | | | Planned |

## 🧠 Topics Covered

**OOP**
- Classes and objects, constructors, properties, access modifiers
- Encapsulation, abstraction, inheritance, polymorphism, interfaces
- Association, aggregation and composition

**Async and multithreading**
- Threads vs tasks, `async`/`await` and the state machine
- `Task.WhenAll` / `WhenAny`, timeouts, cancellation tokens
- `lock`, `SemaphoreSlim`, `Interlocked`, deadlocks and thread-pool starvation
- `Channel<T>`, `IAsyncEnumerable`, `Parallel.ForEachAsync`

**Planned**
- SOLID: SRP, OCP, LSP, ISP, DIP
- Design patterns: Singleton, Factory, Builder, Adapter, Decorator, Facade, Proxy, Strategy, Observer, Command, State
- ASP.NET Core: middleware, dependency injection lifetimes, configuration, filters, exception handling, `IHttpClientFactory`, health checks
- EF Core: change tracking, N+1, loading strategies, transactions, concurrency, indexes

## 🛠 Learning Approach
For each concept:
1. Learn the theory.
2. Understand the real-world problem it solves.
3. Implement a small practical example.
4. Add validation and good design practices.
5. Refactor to improve maintainability.
6. Write notes in my own words.

## ▶️ How to Run
1. Install the .NET SDK.
2. Open the `.sln` file in Visual Studio, or run `dotnet run --project Projects/<ProjectName>`.
3. Some exercises take an argument, for example `dotnet run -c Release -- 3` runs exercise 3.

## 💻 Technologies
C#, .NET, console applications, Git and GitHub

## 🎯 Final Goal
- Write clean, maintainable C# code
- Apply SOLID principles and appropriate design patterns
- Build scalable, secure, well-observed services
- Handle senior-level interview questions on design, async and ASP.NET Core

## 🔄 Continuous Improvement
This repository grows as I learn and practice new topics.