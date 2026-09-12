# Dynamic Categories & Fields — Design Spec

Date: 2026-09-08
Status: Approved by user (pending final spec review)

## Problem

Today, `CategoryType` is a hardcoded 3-value enum (`Biographical`, `Health`,
`Educational`), and every category's field list is a compiled-in
`Dictionary<CategoryType, IReadOnlyList<string>>` (`CategoryCatalog`). Every
field value is stored as `string → string` — there is no field-type concept
(text vs number vs date), no way for a user to add their own category or
field, and no way to group related fields (e.g. an address or a set of
contact numbers) as one structured unit. Adding any new category or field
today requires a code change and redeploy.

The user wants to:
- Create their own categories, and add arbitrary fields to any category.
- Choose a data type per field (not just free text).
- Build composite/structured fields (e.g. "Contact details" holding email,
  telephone, cellphone, work phone as sub-fields) rather than being forced
  into one flat field per piece of information.
- Have rendered form fields carry HTML `name`/`id`/`autocomplete` attributes
  that follow the WHATWG autocomplete-token standard, so vault data can
  eventually be used to autofill forms elsewhere.

## Scope

In scope: Domain/Application/Api/Frontend changes to make categories and
fields data-driven, typed, and nestable one level (groups of scalar fields),
with the 3 existing categories preserved as permanent "system" categories.

Out of scope (explicitly deferred):
- File/attachment field storage backend (blob storage, upload endpoints) —
  the `File` field type is modeled in the schema now, but its storage
  mechanism is a separate follow-up; for this spec a `File` field stores a
  string reference only (e.g. a filename/URL), with no upload pipeline.
- Per-field sharing (sharing stays per-category, matching current behavior).
- Real database persistence (still in-memory; this spec only changes what
  shape the in-memory model takes).
- Recursive nesting beyond one level (groups containing groups) — the data
  model technically allows it via `ParentFieldDefinitionId`, but the UI only
  exposes one level (a group of scalar fields).

## Decisions

These were confirmed with the user during brainstorming:

1. **Category model**: the 3 built-in categories (Biographical, Health,
   Educational) remain permanent, undeletable, unrenamable "system"
   categories. Users can additionally create their own top-level custom
   categories, and add custom fields to any category (system or custom).
2. **Field types**: `Text`, `LongText` (multi-line notes), `Number`, `Date`,
   `Boolean`, `Choice` (user-defined option list), `File` (schema only, no
   upload pipeline yet), `Group` (composite/nesting container).
3. **Composite fields**: freeform — the user builds a group's structure
   themselves (add a `Group` field, then add typed sub-fields inside it).
   No pre-built templates.
4. **HTML standard**: each field may optionally be mapped to one token from
   the WHATWG HTML Living Standard `autocomplete` attribute vocabulary
   (e.g. `email`, `tel`, `street-address`, `bday`, `given-name`). This token
   is stored per field and rendered on the corresponding `<input>`/`<select>`
   alongside a deterministic `name`/`id`.

## Architecture (Approach A, chosen over two alternatives)

Alternatives considered:
- **B — parallel custom-field layer**: keep the enum/catalog untouched,
  bolt on a separate custom category/field system alongside it. Rejected:
  leaves two parallel systems to maintain and requires threading a
  `enum | Guid` union type through Application/Api/sharing forever.
- **C — pure EAV, no system-category flag**: same as A but with no
  `IsSystem` concept at all, enforcing "3 protected categories" purely as a
  business rule with no type-level backing. Rejected: user wants the 3
  built-ins to stay structurally special, which an `IsSystem` flag makes
  simple; C buys no real benefit here.

Chosen — **A: data-driven schema**: replace the enum and compiled-in field
lists with real entities (`Category`, `FieldDefinition`, `FieldValue`),
seed the 3 system categories and their current fields as data, and move
sharing/permissions from keying on the enum to keying on `CategoryId`.

## Domain model

```
Category
  Id: Guid
  Name: string
  IsSystem: bool          // true for the 3 seeded categories; blocks rename/delete

FieldType (enum)
  Text | LongText | Number | Date | Boolean | Choice | File | Group

FieldDefinition
  Id: Guid
  CategoryId: Guid
  ParentFieldDefinitionId: Guid?   // null = top-level; set = nested inside a Group
  Name: string
  FieldType: FieldType
  AutocompleteToken: string?       // validated against the WHATWG token list
  Choices: string[]?               // only meaningful when FieldType = Choice
  SortOrder: int

FieldValue
  FieldDefinitionId: Guid
  VaultId: Guid
  Value: string?                   // always stored as string; parsed/validated per FieldType
```

Notes:
- A `Group` field never has a `FieldValue` of its own — only its children do.
- `AutocompleteToken`, when set, must be one of the fixed WHATWG token
  constants (validated in Domain) so rendered `<input autocomplete="...">`
  stays spec-valid.
- Storing every value as `string` mirrors current behavior exactly (no
  silent type upgrades during seeding) and keeps `FieldValue` simple;
  type-specific parsing/formatting happens in the validation service and
  in the Angular per-`FieldType` control renderer.

## Application layer

**Events** (event-sourced — these become part of vault history):
- `CategoryCreated`, `FieldDefinitionCreated`, `FieldDefinitionUpdated`,
  `FieldDefinitionDeleted` — new schema-mutation events.
- `FieldUpdated` — kept for value changes, now carries `FieldDefinitionId`
  instead of `(CategoryType, string field)`.
- `VaultCreated` — now seeds the 3 system categories + their field
  definitions as part of vault creation (previously via
  `CategoryCatalog.DefaultCategories`).

**Projector** (`VaultProjector`): builds two projections from the event
stream — the schema itself (categories + field definitions, so schema
changes are auditable like any other vault event) and the value store,
now shaped as `Dictionary<Guid /*CategoryId*/, Dictionary<Guid
/*FieldDefinitionId*/, string?>>`.

**Validation service** (replaces `CategoryCatalog.IsValidField`): given a
`FieldDefinitionId`, looks up the field's `FieldType` and validates the
incoming string value:
- `Number` — must parse as a number.
- `Date` — must parse as a date.
- `Boolean` — must be `"true"`/`"false"`.
- `Choice` — must be one of the field's `Choices`.
- `Group` — can never hold a value directly; only its children can.
- `Text`/`LongText`/`File` — any string; no length limit is imposed beyond
  whatever transport-level limit the Api already applies to request bodies.

**Permissions/sharing**: `PermissionGrant`, `CategoryShared`, `ShareRevoked`
move from `CategoryType` to `CategoryId` (Guid). Sharing a category shares
every `FieldDefinition` under it, including nested ones — same
whole-category granularity as today, just generalized to work for custom
categories too.

**`OrganisationDataAccessService`**: today's `CategoryForField(string)`
global reverse lookup assumes field names are unique across the *entire*
catalog — an assumption that breaks once users can create custom fields
with arbitrary names (e.g. two categories both having a "Notes" field).
Replace it with a direct `FieldDefinitionId → CategoryId` lookup (a normal
foreign key), which is a simplification relative to today, not just a port.

**Mutation rules** (all event-sourced, permission-checked as vault-owner
operations — no sharing implications for schema changes themselves):
- Creating a category or field: allowed on the vault owner's own vault.
- Renaming: blocked for `IsSystem` categories; allowed otherwise.
- Deleting a category: blocked if `IsSystem`, or if it has any fields.
- Deleting a scalar field: blocked if it currently has a value.
- Deleting a `Group` field: cascades to delete its children, but only if
  every child is itself deletable (no child currently has a value);
  otherwise the delete is blocked with an error naming the child that has
  data.

## API contracts

- `GET /api/metadata/categories` → returns the full schema:
  ```json
  [{
    "id": "guid", "name": "Biographical", "isSystem": true,
    "fields": [
      { "id": "guid", "name": "Full name", "fieldType": "Text",
        "autocompleteToken": "name", "sortOrder": 0, "children": [] },
      { "id": "guid", "name": "Contact details", "fieldType": "Group",
        "sortOrder": 1, "children": [
          { "id": "guid", "name": "Email", "fieldType": "Text",
            "autocompleteToken": "email", "sortOrder": 0 },
          { "id": "guid", "name": "Cellphone", "fieldType": "Text",
            "autocompleteToken": "tel", "sortOrder": 1 }
        ]}
    ]
  }]
  ```
- `POST /api/categories` — create a custom category (`{ name }`).
- `PUT/DELETE /api/categories/{id}` — rename/delete (system-category and
  non-empty guardrails enforced server-side).
- `POST /api/categories/{id}/fields` — create a field or group
  (`{ name, fieldType, autocompleteToken?, choices?, parentFieldDefinitionId? }`).
- `PUT/DELETE /api/categories/{id}/fields/{fieldId}` — rename/delete a
  field, same guardrails.
- `PUT /api/vault/{categoryId}/fields/{fieldDefinitionId}` — replaces
  today's `UpdateField`; body is still just a string value, validated
  server-side per `FieldType`.
- All route/body params move from the `CategoryType` enum to `Guid`.

## Angular frontend

- `models.ts`'s hardcoded `CategoryType` union is removed; categories and
  fields become runtime data fetched from `/api/metadata/categories`.
- `ui-meta.ts`'s `FIELD_DISPLAY_NAMES`/`CATEGORY_META` stay as an optional
  icon/color lookup, re-keyed by category/field id instead of name, for the
  3 built-ins; `deriveDisplayName` remains the fallback for anything custom
  (already exists today).
- `vault.component.ts`'s single `<input>` per field becomes a small
  per-`FieldType` control renderer: text input, number input, date input,
  checkbox, select (for `Choice`), textarea (for `LongText`). A `Group`
  field renders as a `<fieldset>` containing its children's controls.
- New "Manage categories/fields" UI: add a category; add a field (name,
  type, optional autocomplete token chosen from a dropdown of the WHATWG
  token list, optional choices list for `Choice`); add a `Group` field and
  add fields inside it; rename/delete with the same guardrails as the API
  (disabled/hidden for system categories where applicable).
- Rendered `<input>`/`<select>`/`<textarea>` elements get a deterministic
  `id`/`name` (e.g. `field-{fieldDefinitionId}`, or a readable slug such as
  `contact-details-email` derived from the field's and parent group's
  names) and `autocomplete="{token}"` when the field has one mapped.

## Migration / seeding

Persistence is currently in-memory only, so there is no real data
migration. Startup seeding changes from `CategoryCatalog.DefaultCategories`
to seeding 3 `Category` rows (`IsSystem = true`) plus one `FieldDefinition`
per field name that exists in today's `CategoryCatalog`, all as
`FieldType = Text` — this preserves current behavior exactly, with no
silent type upgrades. The existing `// TODO: Replace with Supabase
Postgres` markers gain more tables to eventually create (`Category`,
`FieldDefinition`, `FieldValue`) but that migration is unchanged in kind.

## Testing

- **Domain**: unit tests for rename/delete guardrails (system category,
  non-empty category, field with a value, group cascade rules) and for
  autocomplete-token validation against the WHATWG token list.
- **Application**: event sourcing/projection tests for the new schema
  events; `CategoryId`-based sharing tests, including that sharing a
  category exposes its nested group fields to the recipient.
- **Api**: integration tests for the new category/field CRUD endpoints and
  for the updated `UpdateField` endpoint using `Guid` identifiers.
- **Frontend**: component tests for the per-`FieldType` control renderer
  and for a `Group` field rendering its children correctly.
