import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { normalizeUrlPathSegment, toSafeHttpsOrigin } from '../helpers/public-url.helper';

@Injectable({
  providedIn: 'root'
})
export class PublicImageUrlService {
  /**
   * Returns a full absolute URL for an image under the configured email-image public path.
   * Example output: https://mydomain.com/assets/email-images/banner.png
   */
  getEmailImageUrl(fileName: string): string {
    const cleanFileName = normalizeUrlPathSegment(fileName);
    if (!cleanFileName) {
      return this.buildAbsoluteUrl(environment.emailImagesPublicPath);
    }
    return this.buildAbsoluteUrl(`${environment.emailImagesPublicPath}/${cleanFileName}`);
  }

  /**
   * Returns full absolute URLs for a group of file names.
   */
  getEmailImageUrls(fileNames: string[]): Record<string, string> {
    return fileNames.reduce<Record<string, string>>((acc, fileName) => {
      acc[fileName] = this.getEmailImageUrl(fileName);
      return acc;
    }, {});
  }

  /**
   * Generic helper when you already have a public path.
   */
  buildAbsoluteUrl(publicPath: string): string {
    const baseOrigin = this.resolvePublicOrigin();
    const normalizedPath = `/${normalizeUrlPathSegment(publicPath)}`;
    return `${baseOrigin}${normalizedPath}`;
  }

  private resolvePublicOrigin(): string {
    const configured = toSafeHttpsOrigin(environment.publicBaseUrl);
    if (configured) {
      return configured;
    }

    if (typeof window !== 'undefined' && window.location?.origin) {
      return toSafeHttpsOrigin(window.location.origin);
    }

    return 'https://localhost';
  }
}
