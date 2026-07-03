# Plan: Flatten plugin image generation to eliminate the namespace/aliasing subsystem

> Status: **Approved — implementing.** Ships as a new minor **v1.5.0** that folds in the pending
> (unreleased) v1.4.2 XPC3007 work; nothing is released before this change.

## Goal

Generate `PreImage`/`PostImage`/`ActionWrapper` the same way Custom APIs generate
`Request`/`Response`/`ActionWrapper`: **uniquely-named types in the plugin's own namespace**,
referenced by **fully-qualified name** in generated handler signatures. This removes the reason the
per-registration namespace exists, which lets us delete the entire aliased-usings subsystem.

A new **XPC2002** error makes the uniqueness precondition explicit at compile time (mirroring the
existing runtime block), so generation can never collide.

---

## 1. New naming scheme

Today (`PluginStepMetadata.cs:52`, `RegisterStepHelper.cs:46`, runtime `Plugin.cs:335-339`):

```
namespace: {PluginNamespace}.PluginRegistrations.{PluginClass}.{Entity}{Operation}{Stage}
types:     PreImage, PostImage, ActionWrapper
```

Proposed (mirrors Custom API at `Plugin.cs:474`):

```
namespace: {PluginNamespace}
prefix:    Sanitize("{PluginClass}{Entity}{Operation}{Stage}")
types:     {prefix}PreImage, {prefix}PostImage, {prefix}ActionWrapper
```

- **Why the plugin-class part is required (and Custom API doesn't need one):** a Custom API name is
  globally unique by construction; an image's identity is the derived tuple `Entity+Operation+Stage`,
  which repeats across plugin classes in the same namespace. Plugin class names are unique within a
  namespace, and — per the registration rule, enforced by XPC2002 below — the tuple is unique within a
  plugin class. So `{PluginClass}{Entity}{Operation}{Stage}` is unique within the namespace. No
  index/disambiguator needed.
- **Sanitization:** route the whole prefix through the existing `IdentifierSanitizer.Sanitize`
  (already used for Custom API names at `Plugin.cs:474`). This also hardens the
  custom-message-operation case (`EventOperation` can be an arbitrary string like
  `"contextand_DoThing"`), which the current raw-concatenation scheme handles only by luck. The
  generator and the runtime must use the identical sanitizer so the reflected type name matches.
- **Verbosity is the accepted trade-off**, identical in spirit to Custom API's `SomeApiRequest`. We do
  **not** steer handlers toward the shared interfaces to avoid the long name — declaring
  `IPluginImage<Account>` throws away the generated typed property wrappers, which are the entire point
  of the feature. The interfaces stay a documented escape hatch for when you genuinely only need the
  raw `Entity`; the docs mention they exist but do not recommend them as the default parameter type.

---

## 2. New diagnostic: XPC2002 — duplicate step registration

- **Descriptor** (`DiagnosticDescriptors.cs`, category XPC2xxx "plugin class structure", **Error**):
  "Plugin '{0}' registers {Entity}/{Operation}/{Stage} more than once. Each entity/operation/stage
  combination may be registered at most once per plugin."
- **Analyzer** `Analyzers/DuplicateStepRegistrationAnalyzer.cs`: register a symbol/compilation-level
  action over each `Plugin`-derived class, collect all `RegisterStep`/`RegisterPluginStep`
  invocations in its constructor, key each by `(sanitized entity, sanitized operation, stage)`, and
  report on the 2nd+ occurrence. Reuse `RegisterStepHelper` for extraction.
- **Rationale:** codifies the existing runtime block at compile time and *guarantees* the naming
  scheme in section 1 is collision-free, so generation never breaks. Error severity because it's
  already illegal.
- Docs: `rules/XPC2002.md`; entry in `AnalyzerReleases.Unshipped.md`.

---

## 3. Source-generator changes

- `Models/PluginStepMetadata.cs:52` — replace `RegistrationNamespace` with
  `TypePrefix => Sanitize($"{PluginClassName}{EntityTypeName}{EventOperation}{ExecutionStage}")`; the
  emit namespace becomes `Namespace` (plugin namespace).
- `Helpers/RegisterStepHelper.cs:46` — update/remove the duplicated namespace formula to match.
- `CodeGeneration/WrapperClassGenerator.cs:29` — emit into `metadata.Namespace`; prefix the three
  class names with `metadata.TypePrefix`. Update per-file hint names to use the prefix so they stay
  unique.
- Confirm the `IActionWrapper` implementation and interface list on the image classes are unchanged
  (only names/namespace move).

## 4. Runtime changes

- `Plugin.cs:335-339` — build the image wrapper type name as
  `$"{ChildClassNamespace}.{IdentifierSanitizer.Sanitize(ChildClassShortName + typeof(TEntity).Name + eventOperation + executionStage)}ActionWrapper"`,
  exactly paralleling the Custom API path at `Plugin.cs:474`. This is the only runtime discovery
  point; it must use the same sanitizer/format as the generator.

## 5. Deletions & simplifications (the payoff)

- **Delete** `CodeFixes/AliasedImageUsingsFixAllProvider.cs`.
- **Remove from `Helpers/SyntaxFactoryHelper.cs`:** `ApplyAliasedImageUsings` (both overloads),
  `ConvertToAliasedUsingsAndQualifyRefs` (all overloads), `ImageAmbiguityRewriter`,
  `DetectImageAmbiguity`, `GetAliasForImageNamespace`, `IsImageRegistrationNamespace`, and the
  `qualifier` overload of `CreateImageParameterList`. Keep `CreateNameofExpression`,
  `BuildSignatureDescription`, and a simplified `CreateImageParameterList` that takes a
  **fully-qualified namespace** (see section 7).
- `AddUsingDirectiveIfMissing` — likely droppable for images (we go fully-qualified instead of adding
  usings); keep only if still used elsewhere.

## 6. Analyzer changes

- `HandlerSignatureMismatchAnalyzer` (XPC4002/4003): update the *expected* image type resolution to
  the new namespace + prefixed names. Comparison stays semantic (by symbol), so a user handler that
  imports the type with a short name still matches. **Also make an unresolved/error-typed image
  parameter count as a mismatch** so post-migration handlers (whose old `PreImage` type no longer
  resolves) trigger the existing code fix rather than only a raw CS0246 — this is the migration hook
  (see section 8).
- `ImageWithoutMethodReferenceAnalyzer` (XPC3003) and `FullEntityImageAnalyzer` (XPC3005):
  naming-independent, no change expected — add a regression test to confirm.

## 7. Code-fix changes (converge on the Custom API pattern)

- `CreateHandlerMethodCodeFixProvider` and `FixHandlerSignatureCodeFixProvider`:
  - Emit **fully-qualified** generated type names (`{PluginNamespace}.{prefix}PreImage`) in the
    handler signature — exactly what the Custom API fixes already do (their tests assert
    `TestNamespace.SomeApiResponse Handle(TestNamespace.SomeApiRequest request)`). No `using`, no
    alias.
  - Swap `GetFixAllProvider()` from `AliasedImageUsingsFixAllProvider` to
    `WellKnownFixAllProviders.BatchFixer` — identical to the Custom API code fixes now.
- `PreferNameofCodeFixProvider`: unaffected by naming; verify.

## 8. Migration strategy (breaking change)

Existing handlers reference short `PreImage`/`PostImage` + `using {Ns}.PluginRegistrations.…;`. After
a rebuild the old namespace is gone, so those break (CS0234 on the using, CS0246 on the params).
Mitigation (pending decision — see "Open questions"):

- **Auto-fix via enhanced XPC4002/4003** (section 6): once the old param type fails to resolve, the
  signature analyzer flags a mismatch and its code fix rewrites the signature to the new
  fully-qualified type; the stale `using` becomes an unused-using (CS8019/IDE0005), cleaned by
  standard tooling. **OR**
- **Documented manual edit only**: rely on the compiler errors + a `docs` migration note.

Note: we deliberately do **not** offer "switch to `IPluginImage<Account>`" as the migration —that
discards the generated typed properties (see section 1). Call the change out prominently as breaking
in the CHANGELOG with a short migration note either way.

## 9. Tests

- `GenerationTests/WrapperClassGenerationTests.cs` — update expected namespace + prefixed names; add a
  case with **two registrations in one plugin** and **two plugins in one namespace** asserting unique,
  collision-free names with no aliasing.
- `DiagnosticTests/FixHandlerSignatureCodeFixProviderTests.cs`,
  `CreateHandlerMethodCodeFixProviderTests.cs` — assert fully-qualified names; **delete**
  aliasing/multi-registration-alias tests.
- New `DiagnosticTests/DuplicateStepRegistrationAnalyzerTests.cs` for XPC2002 (fires on dup; not on
  distinct tuples; not across different plugins).
- New migration regression test: a handler with an unresolved old-style image param triggers XPC4002
  and the fix produces the new fully-qualified signature.
- Full `dotnet test` on both test projects; the whole suite (currently 119 in the SG tests) must stay
  green minus the intentionally-removed aliasing tests.

## 10. Docs & release tracking

- **CLAUDE.md** "Type-Safe Images" section: rewrite the generated-code example to the flat scheme,
  drop the "namespace isolation" bullet, and update the "How It Works" namespace line. Add XPC2002 to
  the diagnostics coverage. Keep the "Image Interfaces" subsection as a documented escape hatch (only
  when you need the raw `Entity`) — mention the interfaces exist but do not present them as the
  recommended handler parameter type.
- `rules/XPC2002.md` new; `AnalyzerReleases.Unshipped.md` add XPC2002.
- **CHANGELOG** (`XrmPluginCore/CHANGELOG.md`): add a `Breaking:` line (the project already uses
  `Breaking:` lines, e.g. v1.3.0).

## 11. Versioning

This changes generated type names + namespaces = breaking for consumers. Ship as **v1.5.0**, and
**rename the pending `### v1.4.2 - Unreleased` heading to `### v1.5.0 - Unreleased`**, folding the
XPC3007 bullets in alongside the new image bullets (1.4.2 is never released on its own). The repo
expresses breaking changes as `Breaking:` lines within minor bumps (precedent: v1.3.0), so add a
prominent `Breaking:` line + migration link under the 1.5.0 heading.

Analyzer release tracking: XPC3007 currently sits in `AnalyzerReleases.Unshipped.md` alongside the
new XPC2002 — both move to `AnalyzerReleases.Shipped.md` under `## Release 1.5.0` when 1.5.0 ships.

## 12. Suggested PR sequence

1. **XPC2002** analyzer + rule doc + tests (independently valuable; establishes the invariant).
2. **Generator + runtime rename** behind the same discovery contract (sections 3-4) + generation
   tests.
3. **Code-fix convergence + subsystem deletion** (sections 5, 7) + analyzer update (section 6) +
   migration hook + test updates.
4. **Docs + CHANGELOG + release tracking** (sections 10-11).

Splitting this way keeps each PR reviewable and green; step 2 is the only one that must land
atomically (generator and runtime must agree on the name format in one commit).

## 13. Open questions / risks

- **Concatenation ambiguity** (`"AB"+"CD…"` vs `"ABC"+"D…"`): theoretically possible with
  separator-less concatenation, but it's the *same* risk the current `{Entity}{Operation}{Stage}`
  scheme already accepts, and now bounded further by XPC2002 + unique class names. If we want it fully
  eliminated, insert an unambiguous separator that's still a valid identifier char (e.g. `_`) between
  segments — cheap, at the cost of an underscore in the type name. **Recommend adding the separator**
  since we're touching this anyway.
- **Custom-message operations**: sanitization must be identical on generator and runtime; covered by
  reusing `IdentifierSanitizer`, but worth an explicit shared test.
- **Non-image registrations**: plugins that register steps *without* images generate no wrapper types
  today; confirm the rename doesn't accidentally start emitting types for them.
