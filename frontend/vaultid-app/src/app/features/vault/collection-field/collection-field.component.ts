import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CollectionItem, FieldDefinition } from '../../../models';
import { fieldMeta } from '../../../ui-meta';
import { FieldControlComponent, FieldEditEvent } from '../field-control/field-control.component';

/** One value changed inside one item of a collection. */
export interface CollectionValueEvent {
  collectionId: string;
  itemId: string;
  fieldId: string;
  value: string;
}

/** The user added, copied or deleted a whole item of a collection. */
export interface CollectionItemEvent {
  collectionId: string;
  /** Absent when adding a blank item; otherwise the item being copied or deleted. */
  itemId?: string;
}

/**
 * Renders a Collection field - a field the user holds several of, such as bank
 * accounts, vehicles or emergency contacts. The collection's children are a
 * template rather than a set of values: every item repeats the same rows, so
 * each item gets its own collapsible sub-card, titled by whichever child the
 * backend marked as the item title (the bank's name, the car's model) and
 * falling back to a numbered label while that value is still blank.
 *
 * Adding and deleting items happens here, but only against a working copy held
 * by the vault screen; nothing is written until the category's Save button
 * sends the collection's full contents in one request. That is also why items
 * carry a client-generated id: the backend treats an id it has never seen as a
 * new item, so an item the user adds and then deletes before saving never
 * reaches the server at all.
 */
@Component({
  selector: 'app-collection-field',
  standalone: true,
  imports: [FieldControlComponent],
  templateUrl: './collection-field.component.html',
  styleUrl: './collection-field.component.css',
})
export class CollectionFieldComponent {
  @Input({ required: true }) field!: FieldDefinition;
  /** The working copy the user is editing, including items not yet saved. */
  @Input({ required: true }) items!: CollectionItem[];
  /** The same collection as last loaded from the backend, used to mark unsaved rows. */
  @Input({ required: true }) storedItems!: CollectionItem[];
  @Input() highlightId: string | null = null;

  @Output() valueChanged = new EventEmitter<CollectionValueEvent>();
  @Output() itemAdded = new EventEmitter<CollectionItemEvent>();
  @Output() itemDuplicated = new EventEmitter<CollectionItemEvent>();
  @Output() itemRemoved = new EventEmitter<CollectionItemEvent>();
  @Output() removed = new EventEmitter<string>();

  readonly expanded = signal(true);
  /** Items the user has collapsed. Everything starts open, as the design shows. */
  private readonly collapsedItems = signal<ReadonlySet<string>>(new Set());

  get displayName(): string {
    return fieldMeta(this.field.name).displayName;
  }

  get anchorId(): string {
    return `fieldrow-${this.field.id}`;
  }

  get isHighlighted(): boolean {
    return this.highlightId === this.field.id;
  }

  /** "2 bank accounts" / "1 vehicle" - the collection's own noun, not a generic "item". */
  get countLabel(): string {
    const noun = this.field.itemNoun?.trim() || 'item';
    const count = this.items.length;
    return `${count} ${count === 1 ? noun : plural(noun)}`;
  }

  get addLabel(): string {
    return `Add ${this.field.itemNoun?.trim() || 'item'}`;
  }

  get emptyLabel(): string {
    return `No ${plural(this.field.itemNoun?.trim() || 'item')} yet.`;
  }

  /**
   * What a collapsed item is called: the value of the child marked as the item
   * title, or a numbered placeholder while that value is still empty, so a
   * freshly added item is never a blank line the user can't tell apart.
   */
  itemTitle(item: CollectionItem, index: number): string {
    const titleField = this.field.children.find((c) => c.isItemTitle) ?? this.field.children[0];
    const value = titleField ? item.fields[titleField.id] : null;
    return value?.trim() || `${capitalise(this.field.itemNoun?.trim() || 'item')} ${index + 1}`;
  }

  isItemExpanded(itemId: string): boolean {
    return !this.collapsedItems().has(itemId);
  }

  toggleItem(itemId: string): void {
    this.collapsedItems.update((collapsed) => {
      const next = new Set(collapsed);
      if (!next.delete(itemId)) {
        next.add(itemId);
      }
      return next;
    });
  }

  /** The item's current values, which `FieldControlComponent` renders as this row's edits. */
  editsOf(item: CollectionItem): Record<string, string> {
    const edits: Record<string, string> = {};
    for (const [fieldId, value] of Object.entries(item.fields)) {
      edits[fieldId] = value ?? '';
    }
    return edits;
  }

  /** The same item as last saved, so each row can tell whether it has been changed. */
  storedValuesOf(itemId: string): Record<string, string | null> {
    return this.storedItems.find((i) => i.itemId === itemId)?.fields ?? {};
  }

  onEdit(itemId: string, edit: FieldEditEvent): void {
    this.valueChanged.emit({ collectionId: this.field.id, itemId, fieldId: edit.fieldId, value: edit.value });
  }

  add(): void {
    this.itemAdded.emit({ collectionId: this.field.id });
  }

  duplicate(itemId: string): void {
    this.itemDuplicated.emit({ collectionId: this.field.id, itemId });
  }

  remove(itemId: string): void {
    this.itemRemoved.emit({ collectionId: this.field.id, itemId });
  }
}

/** Naive English plural, enough for the item nouns the catalog uses ("policy" -> "policies"). */
function plural(noun: string): string {
  if (/[^aeiou]y$/i.test(noun)) {
    return `${noun.slice(0, -1)}ies`;
  }
  return /(s|x|z|ch|sh)$/i.test(noun) ? `${noun}es` : `${noun}s`;
}

function capitalise(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}
