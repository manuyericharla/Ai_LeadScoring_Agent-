import {
  Component,
  ElementRef,
  HostListener,
  Input,
  forwardRef,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let appComboboxUid = 0;

@Component({
  selector: 'app-combobox',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './app-combobox.component.html',
  styleUrl: './app-combobox.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => AppComboboxComponent),
      multi: true
    }
  ]
})
export class AppComboboxComponent implements ControlValueAccessor {
  private readonly host = inject(ElementRef<HTMLElement>);

  @Input() label = '';
  @Input() placeholder = '';
  @Input() required = false;
  @Input() options: string[] = [];
  /** When false, only listed options can be chosen (filters while typing; model does not accept free text). */
  @Input() allowCustomValue = true;

  readonly inputId = `app-combobox-${++appComboboxUid}`;

  value = '';
  /** Scratch text for filtering the list when `allowCustomValue` is false. */
  filterText = '';
  open = false;
  disabled = false;

  private onChange: (v: unknown) => void = () => {};
  private onTouched: () => void = () => {};

  get filteredOptions(): string[] {
    const q = (this.allowCustomValue || !this.open ? this.value : this.filterText).trim().toLowerCase();
    const opts = this.options ?? [];
    if (!q) {
      return opts;
    }
    return opts.filter((o) => o.toLowerCase().includes(q));
  }

  /** True when the suggestion list is shown (omitted when there are zero matches). */
  get panelVisible(): boolean {
    return this.open && !this.disabled && this.filteredOptions.length > 0;
  }

  writeValue(obj: unknown): void {
    if (obj === null || obj === undefined) {
      this.value = '';
      if (!this.allowCustomValue) {
        this.filterText = '';
      }
      return;
    }
    this.value = String(obj);
    if (!this.allowCustomValue) {
      this.filterText = this.value;
    }
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

  @HostListener('document:click', ['$event'])
  onDocumentClick(ev: MouseEvent): void {
    if (!this.host.nativeElement.contains(ev.target as Node)) {
      this.closePanel();
    }
  }

  onInput(ev: Event): void {
    const el = ev.target as HTMLInputElement;
    if (!this.allowCustomValue) {
      this.filterText = el.value;
    } else {
      this.value = el.value;
      this.onChange(this.value);
    }
    this.open = true;
  }

  onInputFocus(): void {
    if (!this.disabled) {
      this.open = true;
      if (!this.allowCustomValue) {
        this.filterText = this.value;
      }
    }
  }

  onBlur(): void {
    this.onTouched();
    queueMicrotask(() => {
      if (!this.host.nativeElement.contains(document.activeElement)) {
        this.closePanel();
      }
    });
  }

  onInputKeydown(ev: KeyboardEvent): void {
    if (ev.key === 'Escape') {
      this.closePanel();
    }
  }

  toggleOpen(ev: MouseEvent): void {
    ev.preventDefault();
    ev.stopPropagation();
    if (this.disabled) {
      return;
    }
    if (this.open) {
      this.closePanel();
    } else {
      this.open = true;
      if (!this.allowCustomValue) {
        this.filterText = this.value;
      }
    }
  }

  selectOption(opt: string, ev: MouseEvent): void {
    ev.preventDefault();
    this.value = opt;
    this.onChange(this.value);
    this.closePanel();
  }

  private closePanel(): void {
    this.open = false;
    if (!this.allowCustomValue) {
      this.filterText = this.value;
    }
  }
}
