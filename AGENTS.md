# Writing style — code comments and XML documentation

This file governs comments and API documentation in C# source. Architecture,
repository layout, and test commands remain documented in `CLAUDE.md`.

## Code comments

Comments explain what the code cannot: an invariant, trade-off, or reason a
non-obvious choice is safe. They do not narrate the implementation or its
history.

- Never reference a PR, issue, fix, previous implementation, or refactor. Git
  history owns that context.
- Do not leave planned behavior in source comments. Track it in an issue,
  requirements document, or decision record.
- Do not commit TODO, FIXME, HACK, or XXX comments.
- “Deliberately not X” comments are encouraged when they prevent a plausible
  but invalid design change.
- Test phase markers (`// Arrange`, `// Act`, `// Assert`) are allowed. Added
  commentary should explain the scenario or invariant.

## XML documentation

XML documentation is part of the package's public contract and appears in
IntelliSense.

- Document every public or protected type and member shipped by a package.
  Interface implementations may use `<inheritdoc />` when their contract is
  unchanged.
- Describe observable behavior rather than restating the symbol name or C#
  signature. Include validation timing, mutation, thread-safety, ordering,
  replacement semantics, and preventable exceptions when relevant.
- Keep `<summary>` concise, normally no more than ten lines. Put extended
  concepts in `<remarks>` or under `docs/`.
- Use `<param>`, `<typeparam>`, `<returns>`, `<exception>`, `<see>`, and `<c>`
  where they make the contract clearer.
- Avoid roadmap language such as “future”, “eventually”, or “for now”. Describe
  the contract implemented by the current package.
