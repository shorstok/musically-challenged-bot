using System;
using System.Collections.Generic;
using System.Linq;
using musicallychallenged.Domain;

namespace musicallychallenged.Services
{
    public class CrypticNameResolver
    {
        private readonly Dictionary<long, string> _namesTaken = new();
        private Stack<string> _namesLeft = new Stack<string>();
        
        private readonly List<string> _namesRepository = new List<string>
        {
            "Винил",
            "Медиатор",
            "Струнодёр",
            "Миниджек",
            "Суфлёр",
            "Смычок",
            "Динамик",
            "Усилок",
            "Комбик",
            "Слайд",
            "Бибоп",
            "Рокстеди",
            "Грувгуру",
            "Шреддер",
            "Горлодранец",
            "Дазитджент",
            "Водичкин",
            "Наушник",
            "Гриф",
            "Прогиб",
            "Анкер",
            "Порожек",
            "Безлад",
            "Пикгард",
            "Педалборд",
            "Плектор",
            "Ретейнер",
            "Страплок",
            "Бридж",
            "Каподастр",
            "Трекер",
            "Потенциометр",
            "Свитч",
            "Квакушка",
            "Флажолет",
            "Харп",
            "Машинка",
            "Тремоло",
            "Кобылка",
            "Битник",
            "Восьмибитник",
            "Ксилофон",
            "Маримба",
            "Вибрафон",
            "Глокеншпиль",
            "Глюкофон",
            "Глитч-хопон",
            "Дудук",
            "Вистл",
            "Сакс",
            "Даксофон",
            "Способинофон",
            "Менестрель",
            "Скальд",
            "Клавинет",
            "Мотет",
            "Вирелэ",
            "Клавир",
            "Орфей",
            "Марксофон",
            "Хёрди-гёди",
            "Монохорд",
            "Ситар",
            "Дутар",
            "Каягым",
            "Леннонивец"
        };

        private readonly object _lock = new object();

        private static readonly Random RandomGenerator = new Random();  

        public static void Shuffle<T>(IList<T> list)  
        {  
            int n = list.Count;  
            while (n > 1) {  
                n--;  
                int k = RandomGenerator.Next(n + 1);  
                T value = list[k];  
                list[k] = list[n];  
                list[n] = value;  
            }  
        }

        public void Reset()
        {
            lock (_lock)
            {
                _namesTaken.Clear();
                
                Shuffle(_namesRepository);

                _namesLeft = new Stack<string>(_namesRepository);
            }

        }

        public CrypticNameResolver()
        {
            Reset();
        }

        public string GetCrypticNameFor(User user)
        {
            lock (_lock)
            {
                if (_namesTaken.TryGetValue(user.Id, out var name))
                    return name;

                if (_namesLeft.Any())
                {
                    var freshName = _namesLeft.Pop();
                    _namesTaken[user.Id] = freshName;

                    return freshName;
                }

                return user.Username ?? user.Name;
            }
        }
    }
}
