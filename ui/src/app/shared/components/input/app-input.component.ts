import { Component, Input, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let appInputUid = 0;

@Component({
  selector: 'app-input',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './app-input.component.html',
  styleUrl: './app-input.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => AppInputComponent),
      multi: true
    }
  ]
})
export class AppInputComponent implements ControlValueAccessor {
  @Input() label = '';
  @Input() placeholder = '';
  @Input() type: 'text' | 'number' | 'email' | 'password' | 'url' = 'text';
  @Input() fullWidth = false;
  @Input() errorMessage = '';
  @Input() min?: number;
  @Input() inputStyle: Record<string, string> | null = null;
  /** ID of a `<datalist>` element for combobox-style suggestions. */
  @Input() listId = '';
  @Input() readOnly = false;

  readonly controlId = `app-input-${++appInputUid}`;

  value: string | number = '';
  disabled = false;

  private onChange: (v: unknown) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(obj: unknown): void {
    if (obj === null || obj === undefined) {
      this.value = '';
      return;
    }
    this.value = obj as string | number;
  }

  registerOnChange(fn: (v: unknown) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  onInput(ev: Event): void {
    const el = ev.target as HTMLInputElement;
    if (this.type === 'number') {
      const raw = el.value;
      this.value = raw === '' ? '' : Number(raw);
    } else {
      this.value = el.value;
    }
    this.onChange(this.value);
  }

  onBlur(): void {
    this.onTouched();
  }

  get displayValue(): string {
    if (this.value === '' || this.value === undefined || this.value === null) {
      return '';
    }
    return String(this.value);
  }
}
