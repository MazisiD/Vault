# VaultID – Claude instructions

## Frontend file structure (Angular)

**Every component lives in its own folder and has three separate files. Never use
inline `template:` or `styles:` in a `@Component` decorator.**

```
src/app/features/<feature>/<component-name>/
  <component-name>.component.ts     # class + @Component metadata only
  <component-name>.component.html   # the template
  <component-name>.component.css    # component-scoped styles
  <component-name>.component.spec.ts
```

The decorator must reference them by URL:

```ts
@Component({
  selector: 'app-example',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './example.component.html',
  styleUrl: './example.component.css',
})
export class ExampleComponent { }
```

Rules:

- No markup in `.ts` files. No CSS in `.ts` files. No `<style>` blocks in `.html` files.
- One component per folder; the folder is named after the component (kebab-case,
  without the `.component` suffix).
- Child components nest inside their parent's folder
  (e.g. `features/vault/field-control/`).
- Routes lazy-load via the full folder path:
  `import('./features/auth/login/login.component')`.
- Global design-system styles stay in `src/styles.css`. Component `.css` files hold
  only styles genuinely scoped to that component.
- When creating a new component, prefer `ng generate component` so this layout is
  produced automatically.

## Backend

- .NET solution under `backend/`, event-sourced domain in `VaultID.Domain`,
  application services in `VaultID.Application`, HTTP surface in `VaultID.Api`.
- Business rules live in the backend. The frontend renders data and surfaces backend
  rejections as messages — it never re-implements a guardrail.

## Commands

```powershell
cd frontend/vaultid-app; npm start                      # dev server
cd frontend/vaultid-app; npx ng build                   # build
cd frontend/vaultid-app; npx ng test --watch=false      # unit tests
cd backend; dotnet build VaultID.slnx                   # backend build
cd backend; dotnet test                                 # backend tests
```
