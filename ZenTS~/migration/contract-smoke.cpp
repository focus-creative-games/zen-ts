#include "../zents-runtime/engine/EngineApi.h"
#include "../zents-runtime/binding/BindingPlan.h"
#include <cassert>
#include <type_traits>
using namespace zents;
using Method = MethodPlan<MarshalPlan>;
using Field = FieldPlan<MarshalPlan>;
using Group = OverloadPlan<Method>;
using Member = MemberPlan<Method, Field, Group>;
using Plan = TypeBindingPlan<Method, Member, Group>;
static_assert(std::is_trivially_copyable<engine::Value>::value, "opaque values are copyable, not implicitly retained");
static_assert(sizeof(engine::Value) == 16, "in-process envelope size changed");
int main() {
    MarshalPlan p{}; p.size=4; p.jsArgSlots=1;
    const MarshalPlan* args[]={&p};
    Method method{}; method.paramsMeta=args; method.arity=1; method.minArity=1;
    Group group; group.candidates.push_back(&method);
    Member member{}; member.kind=MetaKind::MethodDispatch; member.overloadGroup=&group;
    Plan plan{}; plan.ownedNames.push_back("Run");
    plan.instanceMap.emplace(plan.ownedNames.back().c_str(),member);
    assert(plan.instanceMap.at("Run").overloadGroup->candidates.at(0)->paramsMeta[0]->size==4);
    assert(plan.staticMap.empty());
    return 0;
}
