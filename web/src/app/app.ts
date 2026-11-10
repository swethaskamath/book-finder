import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService, FindResult, LibraryItem, PrefList, Preferences, Recommendation } from './api.service';

type Tab = 'taste' | 'recs' | 'find' | 'library';

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected api = inject(ApiService);

  tab = signal<Tab>('taste');
  prefs = signal<Preferences | null>(null);
  recs = signal<Recommendation[] | null>(null);
  found = signal<FindResult | null>(null);
  foundFor = signal('');
  library = signal<LibraryItem[]>([]);
  busy = signal<string | null>(null);
  error = signal<string | null>(null);
  notice = signal<string | null>(null);

  bookInput = '';
  authorInput = '';
  subjectInput = '';
  findTitle = '';
  findAuthor = '';

  constructor() {
    this.run('Loading', this.api.preferences(), p => this.prefs.set(p));
    this.refreshLibrary();
  }

  show(t: Tab) {
    this.tab.set(t);
    if (t === 'library') this.refreshLibrary();
    if (t === 'recs' && this.recs() === null) this.loadRecs();
  }

  // ---- taste
  likeBook() {
    const t = this.bookInput.trim();
    if (!t) return;
    this.run(`Looking up “${t}”`, this.api.likeBook(t), p => { this.prefs.set(p); this.bookInput = ''; this.recs.set(null); });
  }
  unlikeBook(key: string) { this.run('Saving', this.api.unlikeBook(key), p => { this.prefs.set(p); this.recs.set(null); }); }
  add(list: PrefList, field: 'authorInput' | 'subjectInput') {
    const v = this[field].trim();
    if (!v) return;
    this.run('Saving', this.api.addTo(list, v), p => { this.prefs.set(p); this[field] = ''; this.recs.set(null); });
  }
  remove(list: PrefList, v: string) { this.run('Saving', this.api.removeFrom(list, v), p => { this.prefs.set(p); this.recs.set(null); }); }
  hasTaste() { const p = this.prefs(); return !!p && (p.likedBooks.length + p.authors.length + p.subjects.length) > 0; }

  // ---- recommendations
  loadRecs() { this.run('Finding books for you', this.api.recommendations(20), r => this.recs.set(r)); }
  dismiss(r: Recommendation) {
    this.run('Saving', this.api.addTo('seen', r.title), p => {
      this.prefs.set(p);
      this.recs.update(list => list?.filter(x => x.key !== r.key) ?? null);
    });
  }
  lookFor(r: Recommendation) {
    this.findTitle = r.title;
    this.findAuthor = r.authors.split(',')[0] ?? '';
    this.tab.set('find');
    this.find();
  }

  // ---- find & download
  find() {
    const t = this.findTitle.trim();
    if (!t) return;
    this.found.set(null);
    this.run(`Searching free sources for “${t}”`, this.api.findFree(t, this.findAuthor.trim() || undefined), f => {
      this.found.set(f);
      this.foundFor.set(t);
    });
  }
  download(url: string, title: string, format: string) {
    this.run(`Downloading ${title}`, this.api.download(url, title, format), item => {
      this.notice.set(`Saved ${item.fileName} to your library.`);
      this.refreshLibrary();
    });
  }

  // ---- library
  refreshLibrary() { this.api.library().subscribe({ next: l => this.library.set(l), error: () => {} }); }
  size(bytes: number) { return bytes > 1e6 ? `${(bytes / 1e6).toFixed(1)} MB` : `${Math.ceil(bytes / 1e3)} KB`; }
  formats(links: Record<string, string>) { return Object.entries(links); }

  private run<T>(label: string, obs: Observable<T>, next: (v: T) => void) {
    this.busy.set(label);
    this.error.set(null);
    this.notice.set(null);
    obs.subscribe({
      next: v => { next(v); this.busy.set(null); },
      error: (e: HttpErrorResponse) => {
        this.busy.set(null);
        this.error.set(e.error?.message ?? e.error?.detail ?? (e.status === 0 ? 'Cannot reach the BookFinder API. Is it running?' : `Request failed (${e.status}).`));
      },
    });
  }
}
