import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { FieldDefinition } from '../../../models';
import { fieldMeta } from '../../../ui-meta';

/** Emitted when the user changes a leaf field's control value (not yet saved). */
export interface FieldEditEvent {
  fieldId: string;
  value: string;
}

/** Emitted when the user confirms the inline "add sub-field" input on a field row. */
export interface AddSubFieldEvent {
  parentFieldId: string;
  name: string;
}

/**
 * Renders one field definition as a row of the vault's field list, matching
 * `New design/Manage Categories & Fields.dc.html`: a header line carrying the
 * field's name, a sub-field count badge, an expand caret, an "add sub-field"
 * button and a delete button; below it either the field's own control (a leaf)
 * or its indented sub-fields (a group).
 *
 * The control itself is chosen by `fieldType` (dynamic-categories spec):
 * Text/LongText/Number/Date/Boolean/Choice get an editable control with an
 * inline Save button; File is a disabled placeholder (upload isn't
 * implemented - the spec explicitly defers file storage); a Group renders no
 * control of its own, only its children, each rendered recursively by this
 * same component with `nested` set (the backend caps nesting at one level, so
 * recursion never goes deeper than one Group).
 *
 * Every control gets a deterministic `id`/`name` derived from the field (its
 * `fieldDefinitionId` for `id`; a readable slug combining the parent group's
 * name and this field's name for `name`), and an `autocomplete` attribute
 * whenever the field has an `autocompleteToken` set.
 */
@Component({
  selector: 'app-field-control',
  standalone: true,
  // Self-imported so the recursive `<app-field-control>` usage inside a Group's
  // template (rendering its children) resolves - Angular standalone components
  // support this self-reference pattern.
  imports: [FormsModule, NgTemplateOutlet, FieldControlComponent],
  templateUrl: './field-control.component.html',
  styleUrl: './field-control.component.css',
})
export class FieldControlComponent {
  @Input({ required: true }) field!: FieldDefinition;
  @Input({ required: true }) values!: Record<string, string | null>;
  @Input({ required: true }) edits!: Record<string, string>;
  /** Readable slug prefix for this field's `name` attribute - the parent group's name, if any. */
  @Input() idPrefix = '';
  /** True when rendered as one of a Group's children: a compact single-row layout. */
  @Input() nested = false;
  /**
   * Id of the field the user jumped to from the vault search. Setting it
   * expands this row when the target is this field or one of its sub-fields,
   * so a collapsed group can't hide the thing the user just searched for.
   */
  @Input() set highlightId(value: string | null) {
    this.highlightTarget = value;
    if (value && (value === this.field?.id || this.field?.children.some((c) => c.id === value))) {
      this.expanded.set(true);
    }
  }
  get highlightId(): string | null {
    return this.highlightTarget;
  }
  private highlightTarget: string | null = null;

  @Output() edited = new EventEmitter<FieldEditEvent>();
  @Output() saved = new EventEmitter<string>();
  @Output() removed = new EventEmitter<string>();
  @Output() addSubField = new EventEmitter<AddSubFieldEvent>();

  readonly expanded = signal(true);
  readonly addingSubField = signal(false);
  newSubFieldName = '';

  /**
   * A field reads as a group once it has children. The backend promotes a
   * plain field to `Group` on its first sub-field, so `fieldType` alone would
   * also do - checking `children` keeps the row correct even before a reload.
   */
  get isGroup(): boolean {
    return this.field.fieldType === 'Group' || this.field.children.length > 0;
  }

  get subFieldCountLabel(): string {
    const count = this.field.children.length;
    return `${count} ${count === 1 ? 'sub-field' : 'sub-fields'}`;
  }

  get displayName(): string {
    return fieldMeta(this.field.name).displayName;
  }

  get controlId(): string {
    return `field-${this.field.id}`;
  }

  /** Scroll target used when the user picks this field from the vault search. */
  get anchorId(): string {
    return `fieldrow-${this.field.id}`;
  }

  get isHighlighted(): boolean {
    return this.highlightTarget === this.field.id;
  }

  get controlName(): string {
    return slugify(this.idPrefix ? `${this.idPrefix}-${this.field.name}` : this.field.name);
  }

  get childPrefix(): string {
    return this.idPrefix ? `${this.idPrefix}-${this.field.name}` : this.field.name;
  }

  displayValue(): string {
    return this.edits[this.field.id] ?? this.values[this.field.id] ?? '';
  }

  booleanValue(): boolean {
    return this.displayValue() === 'true';
  }

  hasEdit(): boolean {
    return this.edits[this.field.id] !== undefined;
  }

  onInput(value: string): void {
    this.edited.emit({ fieldId: this.field.id, value });
  }

  onCheckbox(checked: boolean): void {
    this.onInput(checked ? 'true' : 'false');
  }

  save(): void {
    this.saved.emit(this.field.id);
  }

  startAddSubField(): void {
    this.newSubFieldName = '';
    this.expanded.set(true);
    this.addingSubField.set(true);
  }

  cancelAddSubField(): void {
    this.addingSubField.set(false);
    this.newSubFieldName = '';
  }

  confirmAddSubField(): void {
    const name = this.newSubFieldName.trim();
    if (!name) {
      return;
    }
    this.addSubField.emit({ parentFieldId: this.field.id, name });
    this.cancelAddSubField();
  }
}

/** Turns a readable name (or group-name/field-name pair) into a lowercase, hyphenated slug for a `name` attribute. */
export function slugify(text: string): string {
  return text
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    // The step above already collapses runs of separators into a single '-',
    // so at most one leading/trailing hyphen can remain.
    .replace(/^-|-$/g, '');
}
