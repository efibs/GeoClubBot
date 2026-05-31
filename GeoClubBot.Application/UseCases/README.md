# UseCases

One **folder per feature slice**. Inside each `<Feature>/` folder:

- the request records (`<Verb><Noun>Command` / `<Verb><Noun>Query`), and
- their handlers (`IRequestHandler<,>`), and
- optional `Validators/` (FluentValidation `AbstractValidator<>`).

Handlers and validators are **auto-registered** via assembly scanning in `Program.cs`
(`IUseCasesAssemblyMarker`) — no DI wiring needed. Handlers return `Result<T>` for
expected failures (see [`../../Documentation/ResultConventions.md`](../../Documentation/ResultConventions.md)).

> Namespace gotcha: this project's `RootNamespace` is `UseCases`, so a file in
> `UseCases/Strikes/` has namespace `UseCases.UseCases.Strikes`.

Full recipes: [`Documentation/DeveloperGuide.md`](../../Documentation/DeveloperGuide.md).
