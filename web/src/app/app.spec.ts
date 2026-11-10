import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';

describe('App', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('loads preferences and shows liked books', async () => {
    const fixture = TestBed.createComponent(App);
    http.expectOne('/api/preferences').flush({
      likedBooks: [{ key: '/w/1', title: 'Dune', author: 'Frank Herbert', subjects: [] }],
      authors: ['Ursula K. Le Guin'], subjects: [], seen: [],
    });
    http.expectOne('/api/library').flush([]);
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Dune');
    expect(text).toContain('Ursula K. Le Guin');
  });

  it('shows a borrow link when no free copy exists', async () => {
    const fixture = TestBed.createComponent(App);
    http.expectOne('/api/preferences').flush({ likedBooks: [], authors: [], subjects: [], seen: [] });
    http.expectOne('/api/library').flush([]);
    const app = fixture.componentInstance;
    app.show('find');
    app.findTitle = 'Modern Bestseller';
    app.find();
    http.expectOne(r => r.url === '/api/free').flush({ copies: [], borrowUrl: 'https://openlibrary.org/w/9' });
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('No free legal copy');
    expect(el.querySelector('a[href="https://openlibrary.org/w/9"]')).toBeTruthy();
  });
});
