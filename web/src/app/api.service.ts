import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface LikedBook { key: string; title: string; author: string; subjects: string[]; }
export interface Preferences { likedBooks: LikedBook[]; authors: string[]; subjects: string[]; seen: string[]; }
export interface Recommendation {
  key: string; title: string; authors: string; year: number | null; coverUrl: string | null;
  freePublicDomain: boolean; score: number; reasons: string[];
}
export interface FreeCopy { source: string; title: string; author: string; links: Record<string, string>; }
export interface FindResult { copies: FreeCopy[]; borrowUrl: string | null; }
export interface LibraryItem { fileName: string; sizeBytes: number; downloadedUtc: string; }
export type PrefList = 'authors' | 'subjects' | 'seen';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  preferences() { return this.http.get<Preferences>('/api/preferences'); }
  likeBook(title: string) { return this.http.post<Preferences>('/api/preferences/books', { title }); }
  unlikeBook(key: string) { return this.http.delete<Preferences>('/api/preferences/books', { params: { key } }); }
  addTo(list: PrefList, value: string) { return this.http.post<Preferences>(`/api/preferences/${list}`, { value }); }
  removeFrom(list: PrefList, value: string) { return this.http.delete<Preferences>(`/api/preferences/${list}`, { params: { value } }); }

  recommendations(count = 20) { return this.http.get<Recommendation[]>('/api/recommendations', { params: { count } }); }

  findFree(title: string, author?: string) {
    let params = new HttpParams().set('title', title);
    if (author) params = params.set('author', author);
    return this.http.get<FindResult>('/api/free', { params });
  }

  library() { return this.http.get<LibraryItem[]>('/api/library'); }
  download(url: string, title: string, format: string) {
    return this.http.post<LibraryItem>('/api/library', { url, title, format });
  }
  fileUrl(name: string) { return `/api/library/${encodeURIComponent(name)}`; }
}
