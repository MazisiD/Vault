import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';
import { VaultApiService } from '../../services/vault-api.service';
import { AuthService } from '../../services/auth.service';
import { SessionService } from '../../services/session.service';
import { Category, CollectionItem, CollectionUpdate, FieldDefinition, FieldType, VaultSummary } from '../../models';
import { categoryMeta, fieldMeta } from '../../ui-meta';
import { AddSubFieldEvent, FieldControlComponent, FieldEditEvent } from './field-control/field-control.component';
import {
  CollectionFieldComponent, CollectionItemEvent, CollectionValueEvent,
} from './collection-field/collection-field.component';

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

/** How the open category's values are laid out: as an editable form, or read-only cards/table. */
export type VaultViewMode = 'form' | 'cards' | 'table';

/** One label/value pair as shown by the read-only Cards and Table views. */
export interface VaultReadRow {
  key: string;
  label: string;
  value: string;
  /** Secret values stay masked in the read-only views - there is no control there to reveal them. */
  secret: boolean;
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
 *
 * Field values are edited as a form and committed a category at a time: rows
 * mark themselves unsaved, and one "Save changes" request sends the whole set
 * so related fields (a group's parts, say) land together instead of leaking
 * out one at a time. The backend still records a separate event per changed
 * field, so the activity feed remains field-level.
 *
 * Collections (bank accounts, vehicles, emergency contacts) are edited against
 * a working copy held here and submitted as their full contents, so adding an
 * item and deleting it again before saving never reaches the server.
 *
 * The same values can be read three ways - as the editable Form, as Cards, or
 * as a Table. Only Form edits; the other two are read-only presentations of
 * exactly the same data.
 *
 * This component only renders data and forwards user actions to the API
 * service - every guardrail (system categories, a field that still holds a
 * value, promoting a populated field to a group, value validation) is enforced
 * by the backend and its rejection is surfaced here as a message, never
 * re-implemented.
 */
@Component({
  selector: 'app-vault',
  standalone: true,
  imports: [FormsModule, FieldControlComponent, CollectionFieldComponent],
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
  /** Collections as last loaded from the backend, keyed by the collection field's id. */
  readonly collections = signal<Record<string, CollectionItem[]>>({});
  /** The user's working copy of those collections, including items not yet saved. */
  readonly draftCollections = signal<Record<string, CollectionItem[]>>({});
  readonly saving = signal(false);
  readonly message = signal('');
  readonly error = signal('');

  /** Form edits; Cards and Table are read-only ways of looking at the same values. */
  readonly viewMode = signal<VaultViewMode>('form');
  readonly viewModes: { id: VaultViewMode; label: string; icon: string }[] = [
    { id: 'form', label: 'Form', icon: 'ph-list' },
    { id: 'cards', label: 'Cards', icon: 'ph-squares-four' },
    { id: 'table', label: 'Table', icon: 'ph-table' },
  ];

  /**
   * Fields whose pending edit genuinely differs from what is stored. Typing a
   * change and undoing it leaves an entry in `edits`, so comparing against
   * `values` is what decides whether there is anything to save.
   */
  readonly dirtyFieldIds = computed(() => {
    const values = this.values();
    return Object.entries(this.edits())
      .filter(([fieldId, value]) => (values[fieldId] ?? '') !== value)
      .map(([fieldId]) => fieldId);
  });

  /**
   * Collections the user has touched. A collection is compared as a whole -
   * items reordered, added, deleted or edited all count - because that is the
   * unit the backend is sent.
   */
  readonly dirtyCollectionIds = computed(() => {
    const stored = this.collections();
    return Object.keys(this.draftCollections())
      .filter((id) => !sameItems(stored[id] ?? [], this.draftCollections()[id] ?? []));
  });

  readonly dirtyCount = computed(() => this.dirtyFieldIds().length + this.dirtyCollectionIds().length);

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
  readonly fieldTypes: FieldType[] = ['Text', 'LongText', 'Number', 'Date', 'Time', 'Link', 'Boolean', 'Choice', 'File', 'Attachment'];
  newFieldName = '';
  newFieldType: FieldType = 'Text';

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
    this.collections.set({});
    this.draftCollections.set({});
    this.saving.set(false);
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
          // Not found -> create it (blueprint 5.1). A repeated create call can
          // race with the first one while the page is still loading, so treat a
          // duplicate-create response as success and then reload the summary.
          this.api.createVault(userId, this.auth.username || userId).subscribe({
            next: (created) => {
              this.summary.set(created);
              this.loadSchema(userId);
            },
            error: (err) => {
              if (err?.status === 409) {
                this.api.getVault(userId).subscribe({
                  next: (existing) => {
                    this.summary.set(existing);
                    this.loadSchema(userId);
                  },
                  error: (retryErr) => this.fail(retryErr, `Could not open a vault for "${userId}".`),
                });
                return;
              }

              this.fail(err, `Could not open a vault for "${userId}".`);
            },
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
    if (categoryId === this.selected()) {
      return;
    }
    if (!this.confirmDiscard()) {
      return;
    }
    this.edits.set({});
    this.selected.set(categoryId);
    this.cancelAddField();
    this.refreshSelected();
  }

  /**
   * Asks before throwing away pending edits. Leaving a category used to wipe
   * them silently, which is far more costly now a whole category is saved in
   * one go.
   */
  private confirmDiscard(): boolean {
    const count = this.dirtyCount();
    if (!count) {
      return true;
    }
    return window.confirm(
      `You have ${count} unsaved change${count === 1 ? '' : 's'} in this category. Leave without saving?`,
    );
  }

  /**
   * Reloads the selected category's stored values. Deliberately leaves `edits`
   * alone: schema changes (adding or deleting a field) also land here, and
   * they must not discard what the user has typed.
   */
  private refreshSelected(): void {
    const categoryId = this.selected();
    const userId = this.userId;
    if (!categoryId || !userId) {
      this.values.set({});
      this.collections.set({});
      this.draftCollections.set({});
      return;
    }
    this.api
      .getCategory(userId, categoryId)
      .pipe(catchError(() => of(null)))
      .subscribe((v) => {
        this.values.set(v?.fields ?? {});
        const stored = v?.collections ?? {};
        this.collections.set(stored);
        // Collections the user is already editing keep their working copy, for
        // the same reason `edits` survives: a schema change must not throw away
        // an item they have half filled in.
        this.draftCollections.update((draft) => {
          const next: Record<string, CollectionItem[]> = {};
          for (const [collectionId, items] of Object.entries(stored)) {
            const working = draft[collectionId];
            next[collectionId] = working && !sameItems(items, working) ? working : cloneItems(items);
          }
          return next;
        });
      });
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

  // --- Collections ---

  /** The working copy of one collection - what the user sees and edits. */
  itemsOf(collectionId: string): CollectionItem[] {
    return this.draftCollections()[collectionId] ?? [];
  }

  /** The same collection as last saved, so each row can tell what has changed. */
  storedItemsOf(collectionId: string): CollectionItem[] {
    return this.collections()[collectionId] ?? [];
  }

  onCollectionValue(event: CollectionValueEvent): void {
    this.updateItems(event.collectionId, (items) =>
      items.map((item) =>
        item.itemId === event.itemId
          ? { ...item, fields: { ...item.fields, [event.fieldId]: event.value } }
          : item,
      ),
    );
  }

  /**
   * Adds a blank item with a client-generated id. The backend treats an id it
   * has not seen as new, so nothing needs to be reserved up front and an item
   * added then deleted before saving never reaches the server.
   */
  addCollectionItem(event: CollectionItemEvent): void {
    this.updateItems(event.collectionId, (items) => [...items, { itemId: newItemId(), fields: {} }]);
  }

  /** Copies an item's values into a new one - a second account at the same bank, say. */
  duplicateCollectionItem(event: CollectionItemEvent): void {
    this.updateItems(event.collectionId, (items) => {
      const source = items.find((i) => i.itemId === event.itemId);
      if (!source) {
        return items;
      }
      return [...items, { itemId: newItemId(), fields: { ...source.fields } }];
    });
  }

  removeCollectionItem(event: CollectionItemEvent): void {
    this.updateItems(event.collectionId, (items) => items.filter((i) => i.itemId !== event.itemId));
  }

  private updateItems(collectionId: string, change: (items: CollectionItem[]) => CollectionItem[]): void {
    this.draftCollections.update((draft) => ({
      ...draft,
      [collectionId]: change(draft[collectionId] ?? []),
    }));
  }

  // --- Reading the category without editing it ---

  isCollection(field: FieldDefinition): boolean {
    return field.fieldType === 'Collection';
  }

  setViewMode(mode: VaultViewMode): void {
    this.viewMode.set(mode);
  }

  /**
   * Every value in the open category flattened to label/value pairs for the
   * Cards and Table views: a group contributes one row per sub-field, and a
   * collection one row per value of each of its items, prefixed so two bank
   * accounts never read as one.
   */
  readonly readRows = computed<VaultReadRow[]>(() => {
    const cat = this.visibleCategory();
    if (!cat) {
      return [];
    }
    const rows: VaultReadRow[] = [];
    for (const field of cat.fields) {
      const label = fieldMeta(field.name).displayName;
      if (field.fieldType === 'Collection') {
        this.itemsOf(field.id).forEach((item, index) => {
          for (const child of field.children) {
            rows.push({
              key: `${item.itemId}-${child.id}`,
              label: `${label} ${index + 1} \u203a ${fieldMeta(child.name).displayName}`,
              value: item.fields[child.id] ?? '',
              secret: !!child.isSecret,
            });
          }
        });
      } else if (field.children.length) {
        for (const child of field.children) {
          rows.push({
            key: child.id,
            label: `${label} \u203a ${fieldMeta(child.name).displayName}`,
            value: this.currentValue(child.id),
            secret: !!child.isSecret,
          });
        }
      } else {
        rows.push({ key: field.id, label, value: this.currentValue(field.id), secret: !!field.isSecret });
      }
    }
    return rows;
  });

  private currentValue(fieldId: string): string {
    return this.edits()[fieldId] ?? this.values()[fieldId] ?? '';
  }

  /** Read-only views never reveal a secret: there is no control there to toggle. */
  readValue(row: VaultReadRow): string {
    if (!row.value) {
      return '\u2014';
    }
    if (!row.secret) {
      return row.value;
    }
    return row.value.length <= 4
      ? '\u2022'.repeat(row.value.length)
      : '\u2022'.repeat(row.value.length - 4) + row.value.slice(-4);
  }


  /**
   * Saves the whole category in one request. The backend validates every value
   * before writing any of them, so a single bad entry rejects the save instead
   * of leaving the category half-written, and it still records one event per
   * changed field so the activity feed names exactly what the user altered.
   * Each touched collection goes along as its complete contents, which is how
   * the backend learns that an item the user deleted is gone.
   */
  saveCategory(cat: Category): void {
    const fieldIds = this.dirtyFieldIds();
    const collectionIds = this.dirtyCollectionIds();
    const changeCount = fieldIds.length + collectionIds.length;
    if (!changeCount || this.saving()) {
      return;
    }
    const edits = this.edits();
    const draft = this.draftCollections();
    const values = fieldIds.map((fieldId) => ({ fieldDefinitionId: fieldId, value: edits[fieldId] }));
    const collections: CollectionUpdate[] = collectionIds.map((collectionId) => ({
      fieldDefinitionId: collectionId,
      items: (draft[collectionId] ?? []).map((item) => ({
        itemId: item.itemId,
        values: Object.entries(item.fields).map(([fieldDefinitionId, value]) => ({
          fieldDefinitionId,
          value: value ?? '',
        })),
      })),
    }));

    this.saving.set(true);
    this.api.updateCategoryFields(this.userId, cat.id, { values, collections }).subscribe({
      next: (view) => {
        this.saving.set(false);
        this.edits.set({});
        this.values.set(view.fields ?? {});
        this.collections.set(view.collections ?? {});
        this.draftCollections.set(cloneCollections(view.collections ?? {}));
        this.error.set('');
        this.message.set(
          `Saved ${changeCount} change${changeCount === 1 ? '' : 's'} to ${cat.name}. ` +
            `Any organisation sharing ${cat.name} now sees them.`,
        );
      },
      error: (err) => {
        this.saving.set(false);
        this.fail(err, 'Could not save those changes. Nothing was written.');
      },
    });
  }

  /** Throws away every pending edit in the category, restoring stored values. */
  discardEdits(): void {
    if (!this.dirtyCount()) {
      return;
    }
    this.edits.set({});
    this.draftCollections.set(cloneCollections(this.collections()));
    this.message.set('');
    this.error.set('');
  }

  unsavedLabel(): string {
    const count = this.dirtyCount();
    return `${count} unsaved change${count === 1 ? '' : 's'}`;
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
    this.newFieldType = 'Text';
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
    this.api.createField(this.userId, cat.id, { name, fieldType: this.newFieldType }).subscribe({
      next: () => {
        this.cancelAddField();
        this.succeed(`Added "${name}" to ${cat.name}.`);
      },
      error: (err) => this.fail(err, 'Could not add that field.'),
    });
  }

  addSubField(cat: Category, event: AddSubFieldEvent): void {
    this.api
      .createField(this.userId, cat.id, {
        name: event.name,
        fieldType: event.fieldType,
        parentFieldDefinitionId: event.parentFieldId,
      })
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

/** Ids for items the user has just added, before the backend has seen them. */
function newItemId(): string {
  return crypto.randomUUID();
}

function cloneItems(items: readonly CollectionItem[]): CollectionItem[] {
  return items.map((item) => ({ itemId: item.itemId, fields: { ...item.fields } }));
}

function cloneCollections(source: Record<string, CollectionItem[]>): Record<string, CollectionItem[]> {
  return Object.fromEntries(Object.entries(source).map(([id, items]) => [id, cloneItems(items)]));
}

/**
 * Whether two versions of a collection hold the same items in the same order
 * with the same values - which is what decides if there is anything to save.
 * A missing value and an empty one are treated alike, because that is how the
 * backend stores them.
 */
function sameItems(left: readonly CollectionItem[], right: readonly CollectionItem[]): boolean {
  if (left.length !== right.length) {
    return false;
  }
  return left.every((item, index) => {
    const other = right[index];
    if (item.itemId !== other.itemId) {
      return false;
    }
    const keys = new Set([...Object.keys(item.fields), ...Object.keys(other.fields)]);
    return [...keys].every((key) => (item.fields[key] ?? '') === (other.fields[key] ?? ''));
  });
}
