# Copilot Instructions

Please chat with me in German and let's be on a first name basis, but still code in US-English.
Remember my personal C# style. For other languages the same principles apply.
- Most of my rules service one purpose: reducing cognitive load when reading code. Modern C# features usually align with this goal, so please prefer them over traditional constructs.
- Never encode implicit knowledge explicitly. I prefer my code short and concise. Consequently, private fields, internal classes etc need no explicit visibility. Wherever possible I'd also prefer targeted new instantiations, variables declared with var and expression based returns rather than statements.
- Please ensure that source code lines are no longer than 100 charaters.
- Prefer sealed types unless it's absolutely clear that we want to keep the type definition open for inheritors.
- Wherever it makes sense I'd prefer usage of records
- Prefer extension types for utility helpers when available; otherwise use static extension classes.
- Prefer collection expressions over explicitly typed expressions including empty collections.
- If it makes the code more concise or elegant, use modern C# features. File scoped namespaces, pattern matching, primary constructors, extension members, spans etc.
- Any class should first contain fields, then properties, then constructors and then methods. These blocks should be sorted by visibility. Public before internal before protected before private. Static members always come after all instance members. Fields start with underscore and are in camelCase. Static or protected fields have no underscore and are in PascalCase.
- Try to minimize negations in conditionals to improve readability.
- Prefer functional expressions over loops wherever possible without sacrificing readability.
- Prefer early returns to reduce cyclomatic complexity.
- Use utility methods and extensions we already have in our code base wherever possible.
- Use strategic blank lines to visually separate logical blocks, error handling, initializations, and return statements. This helps to further improve code readability and makes the structure of methods easier to grasp at a glance.
- In principle SOLID, DRY and YAGNI make a lot of sense.