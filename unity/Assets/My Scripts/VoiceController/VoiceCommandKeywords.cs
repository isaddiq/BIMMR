// VoiceCommandKeywords.cs - AEC Building System Keywords and Actions
using System.Collections.Generic;

public static class VoiceCommandKeywords
{
    public enum VoiceAction
    {
        Hide,
        Show,
        Toggle,
        Spawn,
        Save,
        Load
    }

    // Enhanced AEC Building System Keywords with better Korean support
    public static Dictionary<string, List<string>> BuildingSystemKeywords = new Dictionary<string, List<string>>()
    {
        // Level 1: Primary Building Disciplines
        ["architectural"] = new List<string> {
            "architectural", "architecture", "arch", "building envelope", "architectural system", "architectural elements",
            "건축", "건축물", "건축시스템", "건축구조", "아키텍처", "건축요소", "건축시스템", "건축물시스템"
        },
        ["structural"] = new List<string> {
            "structural", "structure", "frame", "framing", "structural system", "structural elements", "load bearing",
            "구조", "구조물", "구조시스템", "골조", "프레임", "구조요소", "하중지지", "구조체", "구조물시스템"
        },
        ["mep"] = new List<string> {
            "mep", "mechanical", "electrical", "plumbing", "building services", "mep systems", "utilities",
            "엠이피", "기계", "전기", "배관", "설비", "기계전기배관", "유틸리티", "기계설비", "전기설비", "배관설비"
        },

        // Level 2: Architectural Building Components
        ["studs"] = new List<string> {
            "studs", "stud", "wall framing", "metal studs", "structural studs", "wall structure",
            "스터드", "벽골조", "금속스터드", "벽구조", "벽틀", "벽체골조"
        },
        ["exterior walls"] = new List<string> {
            "exterior walls", "exterior wall", "facade", "curtain wall", "cladding", "external walls", "building skin",
            "외벽", "외부벽", "커튼월", "파사드", "외장", "건물외피", "외벽시스템", "외부벽체"
        },
        ["interior walls"] = new List<string> {
            "interior walls", "interior wall", "partitions", "internal walls", "room dividers", "partition walls",
            "내벽", "내부벽", "파티션", "칸막이벽", "내부칸막이", "실내벽", "내벽시스템", "실내벽체"
        },
        ["floor"] = new List<string> {
            "floor", "flooring", "slab", "deck", "floor system", "floor structure", "ground floor",
            "바닥", "슬래브", "마루", "데크", "층", "바닥구조", "바닥시스템", "층바닥"
        },
        ["ceiling"] = new List<string> {
            "ceiling", "ceilings", "suspended ceiling", "drop ceiling", "ceiling system", "overhead",
            "천장", "천정", "매립천장", "드롭천장", "천장시스템", "천장구조"
        },
        ["roof"] = new List<string> {
            "roof", "roofing", "roof structure", "roof deck", "roof system", "rooftop",
            "지붕", "옥상", "루프", "지붕구조", "지붕시스템", "옥상구조"
        },
        ["windows"] = new List<string> {
            "windows", "window", "glazing", "fenestration", "openings", "window systems",
            "창문", "창", "유리창", "윈도우", "개구부", "창문시스템", "창호"
        },
        ["doors"] = new List<string> {
            "doors", "door", "entries", "doorways", "access points", "door systems",
            "문", "도어", "출입문", "입구", "접근점", "문시스템", "출입구"
        },
        ["stairs"] = new List<string> {
            "stairs", "staircase", "steps", "stairway", "stair system", "vertical circulation",
            "계단", "스테어", "층계", "스텝", "수직동선", "계단시스템"
        },
        ["furniture"] = new List<string> {
            "furniture", "furnishing", "fixtures", "equipment", "ff&e", "interior elements",
            "가구", "퍼니처", "비품", "장비", "인테리어요소", "가구시스템"
        },

        // Level 2: Structural Engineering Components
        ["columns"] = new List<string> {
            "columns", "column", "posts", "vertical supports", "structural columns", "pillars",
            "기둥", "컬럼", "수직지지체", "기둥부재", "기둥구조", "기둥시스템"
        },
        ["beams"] = new List<string> {
            "beams", "beam", "girders", "horizontal supports", "structural beams", "floor beams",
            "보", "빔", "거더", "수평지지체", "구조보", "보시스템"
        },
        ["connections"] = new List<string> {
            "connections", "connection", "joints", "fasteners", "structural connections", "bolted connections",
            "접합부", "연결부", "조인트", "체결재", "구조접합", "접합시스템"
        },

        // Level 2: MEP Building Services Components
        ["electric"] = new List<string> {
            "electric", "electrical", "power", "electrical systems", "power distribution", "electrical services",
            "전기", "전력", "전기시스템", "전기설비", "전력배분", "전력시스템", "전기시설"
        },
        ["plumbing"] = new List<string> {
            "plumbing", "pipes", "water systems", "plumbing systems", "water distribution", "sanitary",
            "배관", "파이프", "수도", "급수", "급배수", "위생설비", "배관시스템", "급수시스템"
        },
        ["lighting"] = new List<string> {
            "lighting", "lights", "illumination", "lighting systems", "light fixtures", "luminaires",
            "조명", "라이트", "전등", "조명시스템", "조명기구", "조명설비"
        }
    };

    // Enhanced Prefabricated Module Names with better Korean support
    public static Dictionary<string, List<string>> ModuleKeywords = new Dictionary<string, List<string>>()
    {
        ["installation on site"] = new List<string> {
            "installation", "site installation", "installation on site", "field installation", "construction site",
            "현장설치", "사이트설치", "설치", "현장", "건설현장", "현장 설치", "사이트 설치"
        },
        // Modular construction units - Level 1
        ["m1-1"] = new List<string> {
            "m1-1", "m one one", "module one one", "m11", "module eleven", "unit one one",
            "엠일일", "모듈일일", "엠원원", "유닛일일", "m 1 1", "module 1 1", "module 1-1", "1-1", "1 1",
            "엠 일 일", "모듈 일 일", "엠일대일", "모듈일대일", "엠일에일", "모듈일에일"
        },
        ["m1-2"] = new List<string> {
            "m1-2", "m one two", "module one two", "m12", "module twelve", "unit one two",
            "엠일이", "모듈일이", "엠원투", "유닛일이", "m 1 2", "module 1 2", "module 1-2", "1-2", "1 2",
            "엠 일 이", "모듈 일 이", "엠일대이", "모듈일대이", "엠일에이", "모듈일에이"
        },
        ["m1-3"] = new List<string> {
            "m1-3", "m one three", "module one three", "m13", "module thirteen", "unit one three",
            "엠일삼", "모듈일삼", "엠원쓰리", "유닛일삼", "m 1 3", "module 1 3", "module 1-3", "1-3", "1 3",
            "엠 일 삼", "모듈 일 삼", "엠일대삼", "모듈일대삼", "엠일에삼", "모듈일에삼"
        },
        ["m1-4"] = new List<string> {
            "m1-4", "m one four", "module one four", "m14", "module fourteen", "unit one four",
            "엠일사", "모듈일사", "엠원포", "유닛일사", "m 1 4", "module 1 4", "module 1-4", "1-4", "1 4",
            "엠 일 사", "모듈 일 사", "엠일대사", "모듈일대사", "엠일에사", "모듈일에사"
        },
        // Modular construction units - Level 2
        ["m2-1"] = new List<string> {
            "m2-1", "m two one", "module two one", "m21", "module twenty one", "unit two one",
            "엠이일", "모듈이일", "엠투원", "유닛이일", "m 2 1", "module 2 1", "module 2-1", "2-1", "2 1",
            "엠 이 일", "모듈 이 일", "엠이대일", "모듈이대일", "엠투에일", "모듈투에일"
        },
        ["m2-2"] = new List<string> {
            "m2-2", "m two two", "module two two", "m22", "module twenty two", "unit two two",
            "엠이이", "모듈이이", "엠투투", "유닛이이", "m 2 2", "module 2 2", "module 2-2", "2-2", "2 2",
            "엠 이 이", "모듈 이 이", "엠이대이", "모듈이대이", "엠투에이", "모듈투에이"
        },
        ["m2-3"] = new List<string> {
            "m2-3", "m two three", "module two three", "m23", "module twenty three", "unit two three",
            "엠이삼", "모듈이삼", "엠투쓰리", "유닛이삼", "m 2 3", "module 2 3", "module 2-3", "2-3", "2 3",
            "엠 이 삼", "모듈 이 삼", "엠이대삼", "모듈이대삼", "엠투에삼", "모듈투에삼"
        },
        ["m2-4"] = new List<string> {
            "m2-4", "m two four", "module two four", "m24", "module twenty four", "unit two four",
            "엠이사", "모듈이사", "엠투포", "유닛이사", "m 2 4", "module 2 4", "module 2-4", "2-4", "2 4",
            "엠 이 사", "모듈 이 사", "엠이대사", "모듈이대사", "엠투에사", "모듈투에사"
        },
        // Modular construction units - Level 3
        ["m3-1"] = new List<string> {
            "m3-1", "m three one", "module three one", "m31", "module thirty one", "unit three one",
            "엠삼일", "모듈삼일", "엠쓰리원", "유닛삼일", "m 3 1", "module 3 1", "module 3-1", "3-1", "3 1",
            "엠 삼 일", "모듈 삼 일", "엠삼대일", "모듈삼대일", "엠쓰리에일", "모듈쓰리에일"
        },
        ["m3-2"] = new List<string> {
            "m3-2", "m three two", "module three two", "m32", "module thirty two", "unit three two",
            "엠삼이", "모듈삼이", "엠쓰리투", "유닛삼이", "m 3 2", "module 3 2", "module 3-2", "3-2", "3 2",
            "엠 삼 이", "모듈 삼 이", "엠삼대이", "모듈삼대이", "엠쓰리에이", "모듈쓰리에이"
        },
        ["m3-3"] = new List<string> {
            "m3-3", "m three three", "module three three", "m33", "module thirty three", "unit three three",
            "엠삼삼", "모듈삼삼", "엠쓰리쓰리", "유닛삼삼", "m 3 3", "module 3 3", "module 3-3", "3-3", "3 3",
            "엠 삼 삼", "모듈 삼 삼", "엠삼대삼", "모듈삼대삼", "엠쓰리에삼", "모듈쓰리에삼"
        },
        ["m3-4"] = new List<string> {
            "m3-4", "m three four", "module three four", "m34", "module thirty four", "unit three four",
            "엠삼사", "모듈삼사", "엠쓰리포", "유닛삼사", "m 3 4", "module 3 4", "module 3-4", "3-4", "3 4",
            "엠 삼 사", "모듈 삼 사", "엠삼대사", "모듈삼대사", "엠쓰리에사", "모듈쓰리에사"
        }
    };

    // Enhanced Action keywords with better Korean support
    public static Dictionary<string, VoiceAction> ActionKeywords = new Dictionary<string, VoiceAction>()
    {
        // Basic visibility actions - English
        ["hide"] = VoiceAction.Hide,
        ["show"] = VoiceAction.Show,
        ["display"] = VoiceAction.Show,
        ["reveal"] = VoiceAction.Show,
        ["conceal"] = VoiceAction.Hide,
        ["turn off"] = VoiceAction.Hide,
        ["turn on"] = VoiceAction.Show,
        ["enable"] = VoiceAction.Show,
        ["disable"] = VoiceAction.Hide,
        ["toggle"] = VoiceAction.Toggle,
        ["switch"] = VoiceAction.Toggle,

        // Construction/spawning actions - English
        ["spawn"] = VoiceAction.Spawn,
        ["load"] = VoiceAction.Spawn,
        ["place"] = VoiceAction.Spawn,
        ["install"] = VoiceAction.Spawn,
        ["construct"] = VoiceAction.Spawn,
        ["build"] = VoiceAction.Spawn,
        ["create"] = VoiceAction.Spawn,
        ["add"] = VoiceAction.Spawn,
        ["switch to"] = VoiceAction.Spawn,
        ["change to"] = VoiceAction.Spawn,
        ["activate"] = VoiceAction.Spawn,

        // Project management actions - English
        ["save"] = VoiceAction.Save,
        ["save project"] = VoiceAction.Save,
        ["load project"] = VoiceAction.Load,
        ["restore"] = VoiceAction.Load,

        // Basic visibility actions - Korean (Enhanced)
        ["숨기기"] = VoiceAction.Hide,
        ["숨겨"] = VoiceAction.Hide,
        ["숨기세요"] = VoiceAction.Hide,
        ["감추기"] = VoiceAction.Hide,
        ["감춰"] = VoiceAction.Hide,
        ["감추세요"] = VoiceAction.Hide,
        ["끄기"] = VoiceAction.Hide,
        ["꺼"] = VoiceAction.Hide,
        ["끄세요"] = VoiceAction.Hide,
        ["비활성화"] = VoiceAction.Hide,
        ["비활성화해"] = VoiceAction.Hide,
        ["비활성화하세요"] = VoiceAction.Hide,
        ["보이기"] = VoiceAction.Show,
        ["보여"] = VoiceAction.Show,
        ["보여주세요"] = VoiceAction.Show,
        ["보기"] = VoiceAction.Show,
        ["보세요"] = VoiceAction.Show,
        ["켜기"] = VoiceAction.Show,
        ["켜"] = VoiceAction.Show,
        ["켜세요"] = VoiceAction.Show,
        ["활성화"] = VoiceAction.Show,
        ["활성화해"] = VoiceAction.Show,
        ["활성화하세요"] = VoiceAction.Show,
        ["표시"] = VoiceAction.Show,
        ["표시해"] = VoiceAction.Show,
        ["표시하세요"] = VoiceAction.Show,
        ["토글"] = VoiceAction.Toggle,
        ["토글해"] = VoiceAction.Toggle,
        ["토글하세요"] = VoiceAction.Toggle,
        ["전환"] = VoiceAction.Toggle,
        ["전환해"] = VoiceAction.Toggle,
        ["전환하세요"] = VoiceAction.Toggle,
        ["스위치"] = VoiceAction.Toggle,

        // Construction/spawning actions - Korean (Enhanced)
        ["생성"] = VoiceAction.Spawn,
        ["생성해"] = VoiceAction.Spawn,
        ["생성하세요"] = VoiceAction.Spawn,
        ["생성하기"] = VoiceAction.Spawn,
        ["스폰"] = VoiceAction.Spawn,
        ["스폰해"] = VoiceAction.Spawn,
        ["스폰하세요"] = VoiceAction.Spawn,
        ["로드"] = VoiceAction.Spawn,
        ["로드해"] = VoiceAction.Spawn,
        ["로드하세요"] = VoiceAction.Spawn,
        ["설치"] = VoiceAction.Spawn,
        ["설치해"] = VoiceAction.Spawn,
        ["설치하세요"] = VoiceAction.Spawn,
        ["설치하기"] = VoiceAction.Spawn,
        ["시공"] = VoiceAction.Spawn,
        ["시공해"] = VoiceAction.Spawn,
        ["시공하세요"] = VoiceAction.Spawn,
        ["배치"] = VoiceAction.Spawn,
        ["배치해"] = VoiceAction.Spawn,
        ["배치하세요"] = VoiceAction.Spawn,
        ["불러오기"] = VoiceAction.Spawn,
        ["불러와"] = VoiceAction.Spawn,
        ["불러오세요"] = VoiceAction.Spawn,
        ["바꾸기"] = VoiceAction.Spawn,
        ["바꿔"] = VoiceAction.Spawn,
        ["바꾸세요"] = VoiceAction.Spawn,
        ["변경"] = VoiceAction.Spawn,
        ["변경해"] = VoiceAction.Spawn,
        ["변경하세요"] = VoiceAction.Spawn,
        ["만들기"] = VoiceAction.Spawn,
        ["만들어"] = VoiceAction.Spawn,
        ["만드세요"] = VoiceAction.Spawn,
        ["추가"] = VoiceAction.Spawn,
        ["추가해"] = VoiceAction.Spawn,
        ["추가하세요"] = VoiceAction.Spawn,

        // Project management actions - Korean (Enhanced)
        ["저장"] = VoiceAction.Save,
        ["저장해"] = VoiceAction.Save,
        ["저장하세요"] = VoiceAction.Save,
        ["저장하기"] = VoiceAction.Save,
        ["세이브"] = VoiceAction.Save,
        ["세이브해"] = VoiceAction.Save,
        ["프로젝트저장"] = VoiceAction.Save,
        ["프로젝트 저장"] = VoiceAction.Save,
        ["프로젝트불러오기"] = VoiceAction.Load,
        ["프로젝트 불러오기"] = VoiceAction.Load,
        ["복원"] = VoiceAction.Load,
        ["복원해"] = VoiceAction.Load,
        ["복원하세요"] = VoiceAction.Load
    };
}