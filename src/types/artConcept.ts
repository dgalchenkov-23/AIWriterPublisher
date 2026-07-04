export interface ArtConcept {
  id: number;
  title: string;
  description: string;
  visualElements: string;
  rawPrompt: string;
  aspectRatio: string;
  review: string;
}

export interface BookDescriptionRequest {
  genre: string;
  description: string;
}
