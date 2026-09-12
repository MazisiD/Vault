import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';
import { VaultApiService } from '../../services/vault-api.service';
import { AuthService } from '../../services/auth.service';
import { SessionService } from '../../services/session.service';
import { Category, VaultSummary } from '../../models';
import { categoryMeta, fieldMeta } from '../../ui-meta';
import { AddSubFieldEvent, FieldControlComponent, FieldEditEvent } from './field-control/field-control.component';

/** One field (or sub-field) matching the vault search box, with enough context to jump to it. */
export interface FieldSearchHit {
  categoryId: string;
  categoryLabel: string;
  categoryIcon: string;
  fieldId: string;
  fieldName: string;
  /** Display name of the owning group, when the hit is a sub-field. */
  parentName: string | null;
}

/**
 * "My Vault" view: shows the vault summary and lets the user view/edit fields
 * per category (blueprint 5.1, 5.3). Since the dynamic-categories rewrite the
 * category/field schema is data (fetched from the metadata endpoint) rather
 * than a hardcoded enum, so each field is rendered by `FieldControlComponent`
 * per its `fieldType`.
 *
 * Per `New design/Manage Categories & Fields.dc.html` there is no separate
 * "manage categories & fields" panel: schema editing is inline, as a `+` on
 * the category rail, a `+` in the card header, and per-row add/delete buttons.
 * This component only renders data and forwards user actions to the API
 * service - every guardrail (system categories, a field that still holds a
 * value, promoting a populated field to a group) is enforced by the backend
 * and its rejection is surfaced here as a message, never re-implemented.
 */
@Component({
  selector: 'app-vault',
  standalone: true,
  imports: [FormsModule, FieldControlComponent],
  templateUrl: './vault.component.html',
  styleUrl: './vault.component.css',
})
export class VaultComponent {
  private readonly api = inject(VaultApiService);
  private readonly session = inject(SessionService);
  private readonly auth = inject(AuthService);

  readonly summary = signal<VaultSummary | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly selected = signal<string | null>(null);
  readonly selectedCategory = computed(() => this.categories().find((c) => c.id === this.selected()) ?? null);
  readonly values = signal<Record<string, string | null>>({});
  readonly edits = signal<Record<string, string>>({});
  readonly message = signal('');
  readonly error = signal('');

  /** Free-text vault search, matching field and sub-field names across every category. */
  readonly query = signal('');
  readonly searching = computed(() => this.query().trim().length > 0);
  /** The category card is replaced by the result list while a search is active. */
  readonly visibleCategory = computed(() => (this.searching() ? null : this.selectedCategory()));
  /** Field the user jumped to from a search result - briefly outlined so it is easy to spot. */
  readonly highlighted = signal<string | null>(null);
  private highlightTimer?: ReturnType<typeof setTimeout>;

  /**
   * Names only: the vault deliberately loads values one category at a time, so
   * there is nothing to match a value against for the categories the user
   * isn't looking at, and searching stored values would be a quiet way to leak
   * them into the UI anyway.
   */
  readonly searchResults = computed<FieldSearchHit[]>(() => {
    const needle = this.query().trim().toLowerCase();
    if (!needle) {
      return [];
    }
    const hits: FieldSearchHit[] = [];
    for (const cat of this.categories()) {
      const { icon, label } = categoryMeta(cat.name);
      for (const field of cat.fields) {
        const name = fieldMeta(field.name).displayName;
        if (name.toLowerCase().includes(needle)) {
          hits.push({ categoryId: cat.id, categoryLabel: label, categoryIcon: icon, fieldId: field.id, fieldName: name, parentName: null });
        }
        for (const child of field.children) {
          const childName = fieldMeta(child.name).displayName;
          if (childName.toLowerCase().includes(needle)) {
            hits.push({ categoryId: cat.id, categoryLabel: label, categoryIcon: icon, fieldId: child.id, fieldName: childName, parentName: name });
          }
        }
      }
    }
    return hits;
  });

  readonly addingCategory = signal(false);
  newCategoryName = '';

  readonly addingField = signal(false);
  newFieldName = '';

  private get userId(): string {
    return this.session.userId();
  }

  constructor() {
    // Reload whenever the active user changes. Loading only in the constructor
    // left the screen pinned to whichever id happened to be set when the
    // component was first created, even after the user switched.
    effect(() => {
      const userId = this.session.userId();
      this.reset();
      if (userId) {
        this.loadVault(userId);
        this.loadSchema(userId);
      }
    });
  }

  private reset(): void {
    this.summary.set(null);
    this.categories.set([]);
    this.selected.set(null);
    this.values.set({});
    this.edits.set({});
    this.message.set('');
    this.error.set('');
    this.clearSearch();
    this.cancelAddCategory();
    this.cancelAddField();
  }

  resultCountLabel(): string {
    const count = this.searchResults().length;
    return `${count} ${count === 1 ? 'match' : 'matches'}`;
  }

  clearSearch(): void {
    this.query.set('');
  }

  /**
   * Opens the category a search result belongs to and outlines the field so
   * the user can see where they landed. The highlight is cleared on a timer so
   * it doesn't linger over an otherwise ordinary row.
   */
  goToHit(hit: FieldSearchHit): void {
    this.clearSearch();
    if (hit.categoryId !== this.selected()) {
      this.selectCategory(hit.categoryId);
    }
    this.highlighted.set(hit.fieldId);
    clearTimeout(this.highlightTimer);
    this.highlightTimer = setTimeout(() => {
      if (this.highlighted() === hit.fieldId) {
        this.highlighted.set(null);
      }
    }, 2500);
    // The row only exists once the category card has rendered.
    setTimeout(() => {
      document.getElementById(`fieldrow-${hit.fieldId}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    });
  }

  loadSchema(userId: string = this.userId): void {
    if (!userId) {
      return;
    }
    this.api
      .getCategorySchema(userId)
      .pipe(catchError(() => of([])))
      .subscribe((cats) => {
        this.categories.set(cats);
        // Keep the current selection unless it no longer exists (e.g. it was
        // just deleted), in which case fall back to the first category.
        if (cats.length && !cats.some((c) => c.id === this.selected())) {
          this.selected.set(cats[0].id);
        }
        this.refreshSelected();
      });
  }

  private loadVault(userId: string): void {
    this.api
      .getVault(userId)
      .pipe(catchError(() => of(null)))
      .subscribe((s) => {
        if (s) {
          this.summary.set(s);
        } else {
          // Not found -> create it (blueprint 5.1).
          this.api.createVault(userId, this.auth.username || userId).subscribe({
            next: (created) => {
              this.summary.set(created);
              this.loadSchema(userId);
            },
            error: (err) => this.fail(err, `Could not open a vault for "${userId}".`),
          });
        }
      });
  }

  meta(cat: Category) {
    return categoryMeta(cat.name);
  }

  fieldCountLabel(cat: Category): string {
    return `${cat.fields.length} ${cat.fields.length === 1 ? 'field' : 'fields'}`;
  }

  selectCategory(categoryId: string): void {
    this.selected.set(categoryId);
    this.cancelAddField();
    this.refreshSelected();
  }

  private refreshSelected(): void {
    const categoryId = this.selected();
    const userId = this.userId;
    if (!categoryId || !userId) {
      this.values.set({});
      return;
    }
    this.edits.set({});
    this.api
      .getCategory(userId, categoryId)
      .pipe(catchError(() => of(null)))
      .subscribe((v) => this.values.set(v?.fields ?? {}));
  }

  private fail(err: unknown, fallback: string): void {
    this.message.set('');
    this.error.set((err as { error?: { detail?: string } })?.error?.detail ?? fallback);
  }

  private succeed(text: string): void {
    this.error.set('');
    this.message.set(text);
    this.loadSchema();
  }

  // --- Field values ---

  onEdit(edit: FieldEditEvent): void {
    this.edits.update((e) => ({ ...e, [edit.fieldId]: edit.value }));
  }

  onSave(cat: Category, fieldId: string): void {
    const value = this.edits()[fieldId];
    if (value === undefined) {
      return;
    }
    this.api.updateField(this.userId, fieldId, value).subscribe({
      next: () => {
        this.error.set('');
        this.message.set(`Saved. Any organisation sharing ${cat.name} now sees the change.`);
        this.refreshSelected();
      },
      error: (err) => this.fail(err, 'Could not save that field.'),
    });
  }

  // --- Categories ---

  startAddCategory(): void {
    this.newCategoryName = '';
    this.addingCategory.set(true);
  }

  cancelAddCategory(): void {
    this.addingCategory.set(false);
    this.newCategoryName = '';
  }

  confirmAddCategory(): void {
    const name = this.newCategoryName.trim();
    if (!name) {
      return;
    }
    this.api.createCategory(this.userId, name).subscribe({
      next: (created) => {
        this.cancelAddCategory();
        this.selected.set(created.id);
        this.succeed(`Created category "${name}".`);
      },
      error: (err) => this.fail(err, 'Could not create that category.'),
    });
  }

  deleteCategory(cat: Category): void {
    this.api.deleteCategory(this.userId, cat.id).subscribe({
      next: () => {
        this.selected.set(null);
        this.succeed(`Deleted category "${cat.name}".`);
      },
      error: (err) => this.fail(err, 'Could not delete that category.'),
    });
  }

  // --- Fields ---

  startAddField(): void {
    this.newFieldName = '';
    this.addingField.set(true);
  }

  cancelAddField(): void {
    this.addingField.set(false);
    this.newFieldName = '';
  }

  confirmAddField(cat: Category): void {
    const name = this.newFieldName.trim();
    if (!name) {
      return;
    }
    this.api.createField(this.userId, cat.id, { name }).subscribe({
      next: () => {
        this.cancelAddField();
        this.succeed(`Added "${name}" to ${cat.name}.`);
      },
      error: (err) => this.fail(err, 'Could not add that field.'),
    });
  }

  addSubField(cat: Category, event: AddSubFieldEvent): void {
    this.api
      .createField(this.userId, cat.id, { name: event.name, parentFieldDefinitionId: event.parentFieldId })
      .subscribe({
        next: () => this.succeed(`Added sub-field "${event.name}".`),
        error: (err) => this.fail(err, 'Could not add that sub-field.'),
      });
  }

  removeField(cat: Category, fieldId: string): void {
    this.api.deleteField(this.userId, cat.id, fieldId).subscribe({
      next: () => this.succeed('Field removed.'),
      error: (err) => this.fail(err, 'Could not remove that field.'),
    });
  }
}
