namespace AIWriterPublisher.Api.Models
{
    public class ArtConcept
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;       // Название концепта (например, "Мрачный минимализм")
        public string Description { get; set; } = string.Empty; // Описание идеи для автора на русском
        public string VisualElements { get; set; } = string.Empty; // Ключевые элементы для визуализации
    }

    public class BookDescriptionRequest
    {
        public string Description { get; set; } = string.Empty; // То, что ввела жена/писатель
        public string Genre { get; set; } = string.Empty;       // Жанр (фэнтези, детектив и т.д.)
    }
}