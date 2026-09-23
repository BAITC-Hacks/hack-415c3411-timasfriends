"""Conservative offline checks for obvious junk, not a language or truth detector.

Ordinary short terms, names, numeric goals and RU/KZ/EN text remain accepted.
Unfamiliar words alone are not proof of nonsense; semantic review belongs to AI.
This module deliberately does not import scoring, which consumes these issues.
"""

from collections import defaultdict
import re
import unicodedata

from .models import ContentIssue, Draft


_FIELD_HELP = {
    "title": ("Название", "Кратко назовите задачу. Например: «Помощник проверки работ»."),
    "category": ("Тема", "Укажите направление. Например: «Образование»."),
    "context": ("Контекст", "Опишите текущую ситуацию. Например: «Преподаватели проверяют работы вручную»."),
    "need": ("Потребность", "Напишите, что нужно улучшить. Например: «Сократить время ручной проверки»."),
    "users": ("Пользователи", "Назовите будущих пользователей. Например: «Преподаватели учебного центра»."),
    "data": ("Данные", "Перечислите доступные материалы. Например: «Обезличенные работы и эталоны ответов»."),
    "constraints": ("Ограничения", "Укажите реальные рамки. Например: «Использовать только синтетические данные»."),
    "expectedResult": ("Результат", "Опишите, что передаст команда. Например: «Прототип загрузки работ и таблица оценок»."),
    "successCriteria": ("Критерии успеха", "Укажите способ проверки результата. Например: «Сравнить оценки с эталоном»."),
    "contact": ("Контакт", "Укажите способ связи: почту, телефон или ссылку на профиль."),
    "interactionFormat": ("Взаимодействие", "Опишите формат общения. Например: «Обсуждение вопросов в чате»."),
    "feedbackProcess": ("Обратная связь", "Укажите, кто проверит результат. Например: «Преподаватель проверяет пример работы»."),
}
_UNKNOWN = {
    "не знаю", "пока не знаю", "неизвестно", "не известно", "не указано", "не указан",
    "не указана", "не указаны", "не определено", "не определён", "не определен",
    "не определена", "не определены", "не задано", "нет", "нет данных", "данных нет",
    "нет информации", "нет ответа", "информация отсутствует", "отсутствует", "уточняется",
    "уточнить", "tbd", "todo", "unknown", "not known", "not specified", "not provided",
    "not available", "to be determined", "to be defined", "to be decided", "i don t know",
    "n a", "na", "none", "null", "undefined", "білмеймін", "әзірге білмеймін",
    "белгісіз", "мәлімет жоқ", "деректер жоқ", "көрсетілмеген", "жоқ",
}
_UNKNOWN_PART = "(?:" + "|".join(re.escape(value) for value in sorted(_UNKNOWN, key=len, reverse=True)) + ")"
_ONLY_UNKNOWN = re.compile(rf"{_UNKNOWN_PART}(?:\s+{_UNKNOWN_PART})*")
_WORDS = re.compile(r"[^\W\d_]+", re.UNICODE)
_VOWELS = frozenset("aeiouyаеёиоуыэюяәіөұү")
_KEYBOARD_ROWS = ("qwertyuiop", "asdfghjkl", "zxcvbnm", "йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю")
_KEYBOARD_MARKERS = ("qwert", "asdf", "zxcv", "йцук", "цукен", "фыв", "ывапр", "пролдж", "ячсм")
_TECH_TERMS = {"python", "sql", "crm", "sat", "html", "http", "https", "xml", "css", "json", "grpc", "mqtt", "smtp", "php", "cpp", "csharp"}
_DUPLICATE_FIELDS = frozenset(_FIELD_HELP) - {"title", "category", "contact"}


def _clean(value: str) -> str:
    value = unicodedata.normalize("NFKC", value)
    return "".join(" " if char.isspace() else char for char in value
                   if char.isspace() or not unicodedata.category(char).startswith("C"))


def _normalized(value: str) -> str:
    return re.sub(r"[\W_]+", " ", _clean(value).casefold()).strip()


def _unknown(value: str) -> bool:
    if not value.strip() or value.strip() in {"-", "—", "...", "…", "?"}:
        return True
    return _ONLY_UNKNOWN.fullmatch(_normalized(value)) is not None


def _contact(value: str) -> bool:
    return bool(re.fullmatch(r"https?://[^\s]+", value, re.IGNORECASE)
                or re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", value)
                or (re.fullmatch(r"\+?[\d\s().-]+", value) and sum(char.isdigit() for char in value) >= 5))


def _keyboard_word(word: str) -> bool:
    if len(word) < 4:
        return False
    if word in {"qwer", "йцук", "ячсм"} or (word.startswith("фыв") and len(word) <= 5):
        return True
    # A row marker plus mostly the same keyboard row is stronger evidence than
    # missing dictionary membership; legitimate words such as «провалов» pass.
    if any(marker in word for marker in _KEYBOARD_MARKERS):
        return any(sum(char in row for char in word) / len(word) >= .8 for row in _KEYBOARD_ROWS)
    return False


def _junk_word(raw: str) -> bool:
    word = raw.casefold()
    if word in _TECH_TERMS or len(word) < 4:
        return False
    if re.search(r"(.)\1{3,}", word) or re.fullmatch(r"(.{1,4})\1{2,}", word):
        return True
    if _keyboard_word(word):
        return True
    if raw.isupper() and len(raw) <= 8:
        return False  # Preserve short technical abbreviations and initials.
    if len(word) >= 6 and not any(char in _VOWELS for char in word):
        return True
    return bool(len(word) >= 10 and re.search(r"[^aeiouyаеёиоуыэюяәіөұү]{7,}", word))


def _issue(field: str, code: str, reason: str) -> ContentIssue:
    label, help_text = _FIELD_HELP[field]
    return ContentIssue(field=field, code=code, message=f"{label}: {reason} {help_text}")


def local_message_issues(message: str, field: str = "context") -> list[ContentIssue]:
    """Flag obvious junk without rejecting a merely unfamiliar word or name."""
    field = field if field in _FIELD_HELP else "context"
    if _unknown(message):
        return []
    value = _clean(message).strip()
    if not any(char.isalnum() for char in value):
        return [_issue(field, "no_meaningful_content", "нужны слова или проверяемые данные вместо одних символов.")]
    number_groups = value.split()
    if (field != "contact" and len(number_groups) >= 3
            and all(group.isdecimal() for group in number_groups) and len(set(number_groups)) == 1):
        return [_issue(field, "repeated_content", "повтор одного числа не объясняет содержание поля.")]
    if _contact(value):
        return []
    words = _WORDS.findall(value)
    folded = [word.casefold() for word in words]
    numbers = re.findall(r"\d+(?:[.,]\d+)?", value)
    if len(words) >= 3 and len(set(folded)) == 1 and len(set(numbers)) <= 1:
        return [_issue(field, "repeated_content", "повтор одного слова не объясняет содержание поля.")]
    if len(words) >= 6 and all(len(word) == 1 for word in words) and _junk_word("".join(words)):
        return [_issue(field, "gibberish", "похоже на случайный набор букв.")]
    suspicious = [word for word in words if _junk_word(word)]
    if suspicious and sum(map(len, suspicious)) >= sum(map(len, words)) * .5:
        return [_issue(field, "gibberish", "похоже на случайные буквы или повторы; смысл ответа неясен.")]
    return []


def local_draft_issues(draft: Draft) -> list[ContentIssue]:
    """Review each field and catch copy-pasted filler across unrelated fields."""
    issues = []
    duplicates = defaultdict(list)
    for field, value in draft.model_dump().items():
        issues.extend(local_message_issues(value, field))
        normalized = _normalized(value)
        if field in _DUPLICATE_FIELDS and len(normalized) >= 4 and not _unknown(value):
            duplicates[normalized].append(field)
    flagged = {issue.field for issue in issues}
    for fields in duplicates.values():
        if len(fields) < 4:
            continue
        for field in fields:
            if field not in flagged:
                issues.append(_issue(field, "duplicate_content", "один текст повторён в нескольких разных полях; уточните именно это поле."))
    return issues
