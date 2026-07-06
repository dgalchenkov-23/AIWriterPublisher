export interface CharacterGenerationRequest {
  UserDescription: string;
  IsFullBody: boolean;
  FaceReferenceUrl?: string | null;
}

export interface CharacterGenerationResponse {
  GeneratedPrompt: string;
  AgentReview: string;
  ImageUrl: string;
}